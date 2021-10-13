using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Reflection;
using System.Text;

namespace JsonToCuplLib
{
    /// <summary>
    /// WinCUPL Code generator
    /// Converts Yosys generated JSON file into CUPL
    /// </summary>
    public class CodeGenCupl : CodeGenBase
    {
        readonly ContainerNode _mod;
        readonly HashSet<Node> _visited = new HashSet<Node>();
        readonly HashSet<Node> _createdPinNodes = new HashSet<Node>();
        readonly HashSet<Node> _createdPins = new HashSet<Node>();
        readonly IConfig _config;

        //Explicitly use \r\n instead of using the Environment.NewLine.
        const string ENDLINE = "\r\n";

        public CodeGenCupl(ContainerNode mod, IConfig config)
        {
            _mod = mod;
            _config = config;
        }

        /// <summary>
        /// Creates initial PINs and PINNODEs
        /// </summary>
        public void GenerateBranchingNodes()
        {
            CreatePins();
            CreateBranchingPinNodes(_mod);
            RebuildConnections();
            CheckConnections();
        }

        /// <summary>
        /// Finds redundant node connections and eliminates them
        /// </summary>
        public void SimplifyConnections()
        {
            Simplify();
            RebuildConnections();
            CheckConnections();
        }

        /// <summary>
        /// Collapses DFFs, Latches, and TBUFs into ether a pin or pinnode
        /// In WinCUPL, these components are inferred by the type of input connection name.
        /// 
        /// For example, if we have PIN|PINNODE a 
        /// 
        /// a.OE = .... output enable of pin a, this node is tri-state
        /// a.D = .... D value of a d flip flop
        /// a.CK = .... clock value of the flip flop
        /// a.AR = ..... Async reset of the flip flop
        /// a.AP = ..... Async preset of the flip flop 
        /// 
        /// 
        /// 
        /// Another example, if we have a PINNODE b
        /// 
        /// b.OE = ..... tri-state output enable for the latch
        /// b.L = .... latch data value
        /// b.LE = .... latch enable value
        /// 
        /// Latches appear to not have async reset\preset in WINCUPL
        /// 
        /// </summary>
        public void GenerateCollapseNodes()
        {
            CollapseTriStateBuffers();
            RebuildConnections();
            CollapseRegisters();
            RebuildConnections();
        }

        /// <summary>
        /// Emits WinCUPL mumbo jumbo
        /// </summary>
        /// <param name="tr"></param>
        public override void GenerateCode(TextWriter tr)
        {
            WriteHeader(tr);
            WriteGroupSeparator(tr);
            WritePinDefinitions(tr);
            WriteGroupSeparator(tr);
            WritePinNodeDefinitions(tr);
            WriteGroupSeparator(tr);
            WriteExpressions(tr);
        }

        public void FixPinNames()
        {
            foreach (Node pin in _createdPins)
            {
                pin.Name = ScrubPinName(pin.Name);
            }

            foreach (Node pinNode in _createdPinNodes)
            {
                pinNode.Name = ScrubPinName(pinNode.Name);
            }
        }

        /// <summary>
        /// Created duplicates of PIMNODES that are purely combinational logic and replaces reference to those nodes with the duplicated expression
        /// PINNODES that contain feedback to themselves are excluded.  PINNODES that have the least complexity ( [number of terms] * [number of references] ) are chosen for duplication
        /// This is set by using the LimitCombinationalPinNodes function.
        /// 
        /// This feature is useful for devices that do not support buried pinnodes and require an actual external pin assignment, excluding that pin for IO 
        /// 
        /// TOOD: Consider splitting this up
        /// </summary>
        public void ExpandCombinationalPinNodes()
        {
            if (_config.LimitCombinationalPinNodes == null || _config.LimitCombinationalPinNodes < 0)
            {
                return;
            }
            int limitCombin = _config.LimitCombinationalPinNodes.Value;

            Node removePinNode;
            while ((removePinNode = GetNextCollapsedCombinLogicPinNode(limitCombin)) != null)
            {
                //Contains the target input (input connection the pinnode outputs to) and the new cloned output connection of the pinnode expression
                List<Tuple<PinConnection, PinConnection>> replacePinNodeRef = new List<Tuple<PinConnection, PinConnection>>();

                _createdPinNodes.Remove(removePinNode);

                PinConnection inputToPinNode = removePinNode.Connections.GetInputs().First();
                PinConnection expressionOutput = inputToPinNode.Refs[0];
                PinConnection outputPinNode = removePinNode.Connections.GetOutput();

                List<Node> combinNodes = new List<Node>();
                List<Node> _ = new List<Node>();
                List<Node> terminalNodes = new List<Node>();

                ScanExpressionNodes(expressionOutput, combinNodes, _, false, terminalNodes);

                foreach (PinConnection targetConInput in outputPinNode.Refs)
                {
                    //If the input is part of a feedback expression, then skip duplication
                    //TODO: this should never happen, we exclude pinnodes that have feedback
                    if (combinNodes.Contains(targetConInput.Parent))
                        continue;

                    List<Node> nodesOld = new List<Node>();
                    List<Node> nodesNew = new List<Node>();
                    List<Node> terminalNode = new List<Node>();

                    //Duplicate the express, so PINNODE A = (b & c) # d, the express (b & c) # d will be cloned 
                    ScanExpressionNodes(expressionOutput, nodesOld, nodesNew, true, terminalNode);

                    //Find new expression output connection
                    PinConnection newExpressionOutput = nodesNew.Where(x => x.Name == expressionOutput.Parent.Name).First().Connections.GetOutput();

                    //replace the pinnodes target output with newExpressionOutput
                    replacePinNodeRef.Add(new Tuple<PinConnection, PinConnection>(targetConInput, newExpressionOutput));

                    //We are trying to build the connection references (using the equivalent oldNode as a reference).
                    foreach (Node dup in nodesNew)
                    {
                        //Find matching old node
                        Node oldNode = nodesOld.Where(x => x.Name == dup.Name).First();

                        //Loop though each input of the old node
                        foreach (PinConnection inputConOld in oldNode.Connections.GetInputs())
                        {
                            //Input to new node 
                            PinConnection inputConNew = dup.Connections.GetInputs().First(x => x.Name == inputConOld.Name);

                            //Old output ref
                            PinConnection outConOld = inputConOld.Refs[0];

                            //Old output node
                            Node outNodeOld = outConOld.Parent;

                            //Attempt to find the corresponding output node (for inputConNew) within newNodes
                            Node foundOutNode = nodesNew.FirstOrDefault(x => x.Name == outNodeOld.Name);

                            PinConnection outRef = null;

                            //found output ref node is not within duplicated node list (it is not an output of the cloned combinational logic), it is external
                            if (foundOutNode == null)
                            {
                                //This is the pinnode, we are eliminating the pinnode.  targetConInput is the reference input connection to the pinnodes output, so its parent node output is what we need
                                //for the inputConNews reference
                                if (outNodeOld == removePinNode)
                                {
                                    //TODO: Not sure it needs to be a bidirectional, this would be tri-state value within buried combinational logic
                                    outRef = targetConInput.Parent.Connections.GetOutputOrBidirectional();
                                }
                                else
                                {
                                    //TODO: Not sure it needs to be a bidirectional, this would be tri-state value within buried combinational logic
                                    outRef = outNodeOld.Connections.GetOutputOrBidirectional();
                                }
                            }
                            else
                            {
                                outRef = foundOutNode.Connections.GetOutput(); //No way its bidirectional
                            }
                            inputConNew.Refs.Add(outRef);
                            outRef.Refs.Add(inputConNew);
                        }
                    }
                }
                //Search for references of the pinnode, replace with cloned expression
                HashSet<Node> referencedNodes = new HashSet<Node>();
                foreach (PinConnection con in removePinNode.Connections)
                {
                    foreach (PinConnection conRef in con.Refs)
                    {
                        referencedNodes.Add(conRef.Parent);
                    }
                    con.Refs.Clear();
                }
                foreach (Node nodeRef in referencedNodes)
                {
                    foreach (PinConnection con in nodeRef.Connections)
                    {
                        int num = con.Refs.RemoveAll(x => removePinNode == x.Parent);
                    }
                }
                foreach (var updateRef in replacePinNodeRef)
                {
                    updateRef.Item1.Refs.Add(updateRef.Item2);
                    updateRef.Item2.Refs.Add(updateRef.Item1);
                }
            }
        }

        /// <summary>
        /// Looks though the module connections where the parent node is a module.
        /// If the parent node is a module, then this connection must be a PIN
        /// </summary>
        void CreatePins()
        {
            foreach (PinConnection con in _mod.Connections.Where(c => c.Parent.Type == NodeType.Module))
            {
                Node pin = con.Parent = new Node(con.Name, NodeType.Pin);
                pin.Connections.Add(con);
                AddPin(pin);
            }
        }

        /// <summary>
        /// Loops though created pins and pinNodes and adds a connection within the module, this way it can become traversable during code generation
        /// TODO: The code generator is using this function too much.  We should only do this when a pin or pinnode is created, and we could perform a
        /// add/remove connection when adding and removing pin/pinnodes
        /// </summary>
        void RebuildConnections()
        {
            _mod.Connections.Clear();
            foreach (Node cell in _createdPins)
            {
                CheckNode(cell);
                AddInputsToModuleConnection(cell);
            }

            //Delete pinNodes that have no output references
            _createdPinNodes.RemoveWhere(x =>
            {
                PinConnection outCon = x.Connections.GetOutput();
                if (outCon == null || outCon.Refs.Count == 0)
                    return true;
                else
                    return false;
            });
            foreach (Node cell in _createdPinNodes)
            {
                CheckNode(cell);
                AddInputsToModuleConnection(cell);
            }
            foreach (Node cell in _mod.Cells.Where(c => c.Type.IsDFFOrLatch() || c.Type == NodeType.TBUF))
            {
                CheckNode(cell);
                AddInputsToModuleConnection(cell);
            }
        }

        void CheckConnections()
        {
            foreach (Node cell in _createdPins)
            {
                CheckNode(cell);
            }
            foreach (Node cell in _createdPinNodes)
            {
                CheckNode(cell);
            }

            foreach (var node in _mod.Cells)
            {
                CheckNode(node);
            }
        }

        /// <summary>
        /// Loops though each nodes input connections, if there are no references to that input internally within the node graph, then it must come externally
        /// and is considered a top level input connection
        /// </summary>
        /// <param name="cell"></param>
        void AddInputsToModuleConnection(Node cell)
        {
            foreach (PinConnection inputConnection in cell.Connections.GetInputsOrBidirectional())
            {
                if (inputConnection.Refs.Count > 0)
                    _mod.Connections.Add(inputConnection);
            }
        }

        /// <summary>
        /// Collapses tri-state buffers into their respective PIN or PINNODE
        /// If TBUF's output is referenced by more than one node, it attempts to find a suitable
        /// PIN candidate.
        ///
        /// If a single candidate is not found, then a PINNODE is created in its place.
        /// </summary>
        void CollapseTriStateBuffers()
        {
            foreach (Node node in _mod.Cells.Where(c => c.Type == NodeType.TBUF))
            {
                PinConnection output = node.Connections.GetOutput();
                if (output.Refs.Count == 0)
                    continue;
                Node mergeTo = GetMergeToTarget(output);
                Assert((mergeTo.NodeProcessState & NodeProcessState.MergeTBUF) == 0, $"Node {mergeTo.Name} already contains TBUF inputs");
                PinConnection[] inputsToMergeTo = mergeTo.Connections.GetInputsOrBidirectional().ToArray();
                Assert(inputsToMergeTo.Length == 1, "mergeTo node contains multiple inputs");
                PinConnection mergeToInput = inputsToMergeTo[0];
                Assert(mergeToInput.Refs.Contains(output), "Top level node does not contain TBUF output");
                mergeToInput.Refs.Clear();
                foreach (PinConnection inputToTBUF in node.Connections.GetInputs())
                {
                    PinConnection outputToInputToMerge = inputToTBUF.Refs[0];
                    if (inputToTBUF.Name == "A")
                    {
                        //If inputsToMerge is the value part of the TBUF, attach the referenced output to the input pin
                        //we are merging instead of just adding the connection
                        mergeToInput.Refs.Add(outputToInputToMerge);
                        outputToInputToMerge.Refs.Remove(inputToTBUF);
                        outputToInputToMerge.Refs.Add(mergeToInput);
                    }
                    else
                    {
                        inputToTBUF.Parent = mergeTo;
                        mergeTo.Connections.Add(inputToTBUF);
                    }
                }

                UpdateReplacementNode(mergeTo, output);

                mergeTo.NodeProcessState |= NodeProcessState.MergeTBUF;
                output.Refs.Clear();
                node.Connections.Clear();
                CheckConnections();
            }
        }

        /// <summary>
        /// Collapses latches and flip flops into their respective PIN or PINNODE.  If a single candidate is not found, then a PINNODE is created in its place.
        /// 
        /// After this operation, there should be no more nodes of type DFF or Latch, they all should have been merged into a respective PIN or PINNODE
        /// </summary>
        void CollapseRegisters()
        {
            foreach (Node node in _mod.Cells.Where(c => c.Type.IsDFFOrLatch()))
            {
                PinConnection output = node.Connections.GetOutput();
                if (output.Refs.Count == 0)
                    continue;

                Node mergeTo = GetMergeToTarget(output);

                PinConnection mergeToInput;
                //If mergeTo already is a DFF, do not merge, create another PinNode
                if ((mergeTo.NodeProcessState & NodeProcessState.MergeRegister) != 0)
                {
                    mergeTo = CreatePinNodeForOutput(output);
                    AddPinNode(mergeTo);
                    mergeToInput = mergeTo.Connections.GetInputs().First();
                }
                //We can only merge into a node processed as a tbuf if the dff output goes to the tbuf input (not the output enable)
                //If the output is the OE of the tbuf, or the output of this dff is referenced by more than one node, then we need a new PINNODE
                else if ((mergeTo.NodeProcessState & NodeProcessState.MergeTBUF) != 0)
                {
                    mergeToInput = mergeTo.Connections.Find(c => c.Refs.Contains(output));
                    if (mergeToInput.Name == "OE" || output.Refs.Count > 1)
                    {
                        mergeTo = CreatePinNodeForOutput(output);
                        AddPinNode(mergeTo);
                        mergeToInput = mergeTo.Connections.GetInputs().First();
                    }
                }
                //mergeTo is just a plain old PIN\PINNODE, we can merge into this node.
                else
                {
                    //Check to make sure there are not multiple inputs
                    PinConnection[] mergeToInputs = mergeTo.Connections.GetInputs().ToArray();
                    Assert(mergeToInputs.Length == 1, "Inconsistent number of inputs in merge to node");
                    mergeToInput = mergeToInputs[0];
                }


                //Clear this input reference (which is the output of node)
                mergeToInput.Refs.Clear();

                //Remove this input from mergeTo
                mergeTo.Connections.Remove(mergeToInput);

                //We need to merge the DFF nodes inputs into the mergeTo node
                foreach (PinConnection inputsToMerge in node.Connections.GetInputs())
                {
                    mergeTo.Connections.Add(inputsToMerge);
                    inputsToMerge.Parent = mergeTo;
                }

                //All values that referenced the DFF's output need to be changed to the mergeTo's output connection
                UpdateReplacementNode(mergeTo, output);

                mergeTo.NodeProcessState |= NodeProcessState.MergeRegister;

                //DFF's output and connections should no longer be referenced
                output.Refs.Clear();
                node.Connections.Clear();
                CheckNode(node);
                CheckNode(mergeTo);
            }
        }

        /// <summary>
        /// For DFFs, latches, or tri-state buffers.  Attempts to find a suitable pin or pinnode to collapse to.  If no suitable pinnode or pin is found, then a 
        /// pinnode is created (it becomes buried logic)
        /// </summary>
        /// <param name="output"></param>
        /// <returns></returns>
        Node GetMergeToTarget(PinConnection output)
        {
            Node mergeTo;
            //If this dff or latch references more than one node, find a node candidate that we can merge to
            if (output.Refs.Count > 1)
            {
                //Look for a PIN
                Node[] foundPins = output.Refs.Select(r => r.Parent).Where(n => n.Type == NodeType.Pin).ToArray();
                if (foundPins.Length == 1)
                {
                    mergeTo = foundPins[0];
                }
                else
                {
                    //We reference to more than one node, so create a pinnode and merge into that
                    mergeTo = CreatePinNodeForOutput(output);
                    AddPinNode(mergeTo);
                }
            }
            else
            {
                mergeTo = output.Refs[0].Parent;
                if (mergeTo.Type != NodeType.Pin && mergeTo.Type != NodeType.PinNode)
                {
                    mergeTo = CreatePinNodeForOutput(output);
                    AddPinNode(mergeTo);
                }
            }
            return mergeTo;
        }

        /// <summary>
        /// Makes sure that referenced connections are proper (output connection is not within the references of another output connection)
        /// </summary>
        /// <param name="node"></param>
        void CheckNode(Node node)
        {
            PinConnection output = node.Connections.GetOutput();
            if (output != null)
            {
                foreach (var inputRefOutput in output.Refs)
                {
                    Assert(inputRefOutput.InputOrBidirectional, "Non input connection referenced by output");
                    Assert(inputRefOutput.Refs.Contains(output), "Input connection does not reference required output connection");
                }
            }

            foreach (var input in node.Connections.GetInputsOrBidirectional())
            {
                foreach (var outputRefInput in input.Refs)
                {
                    Assert(outputRefInput.DirectionType == DirectionType.Output, "Non output connection referenced by input");
                    Assert(outputRefInput.Refs.Contains(input), "Output connection does not reference required input connection");
                }
            }
        }



        /// <summary>
        /// Recursively walks though all input connections of a node
        /// If the corresponding output connection references more than one node, then an
        /// a pinnode is created.
        ///
        /// This is done so non repeated combinational logic is generated per CUPL expression.
        ///  
        /// </summary>
        /// <param name="node"></param>
        void CreateBranchingPinNodes(Node node)
        {
            if (node.Type == NodeType.PinNode || _visited.Contains(node)) return;
            _visited.Add(node);
            foreach (PinConnection connection in node.Connections.Where(c => c.InputOrBidirectional))
            {
                if (connection.Refs.Count == 0) continue;
                Assert(connection.Refs.Count == 1, "Invalid number of connections to input node " + connection.Name);
                PinConnection output = connection.Refs[0];
                Assert(output.OutputOrBidirectional == true, "Unknown connection to input node " + connection.Name);
                Node parentOutputNode = output.Parent;
                //Do not generate pinnodes for non combinational logic nodes.
                if (parentOutputNode.Type != NodeType.PinNode &&
                    !parentOutputNode.Type.IsDFFOrLatch() &&
                    parentOutputNode.Type != NodeType.TBUF &&
                    parentOutputNode.Type != NodeType.Pin &&
                    output.Refs.Count > 1)
                {
                    Node pinNode = CreatePinNodeForOutput(output);
                    AddPinNode(pinNode);
                }
                CheckConnections();
                CreateBranchingPinNodes(parentOutputNode);
            }
        }

        /// <summary>
        /// Creates a pinnode for a specified output connection.
        /// Replaces original references from the old output connection to the new output connection of the
        /// created pinnode
        /// </summary>
        /// <param name="oldOutputConnection">Output connection to be replaced with the pinnode</param>
        /// <returns>An new pinnode</returns>
        Node CreatePinNodeForOutput(PinConnection oldOutputConnection)
        {
            Assert(oldOutputConnection.OutputOrBidirectional, $"Cannot create PinNode on node {oldOutputConnection.Parent.Name}.{oldOutputConnection.Name}");
            string newName = oldOutputConnection.Parent.Type.IsDFFOrLatch() ? oldOutputConnection.Parent.Name : Util.GenerateName();
            Node pinNode = new Node(newName, NodeType.PinNode);
            PinConnection newOutputConnection = new PinConnection(pinNode, "_PIN_OUT", DirectionType.Output);
            PinConnection newInputConnection = new PinConnection(pinNode, "_PIN_IN", DirectionType.Input);
            pinNode.Connections.Add(newOutputConnection);
            pinNode.Connections.Add(newInputConnection);

            //For each input connection that references the oldOutputConnection connection
            //1. replace old reference with newOutputConnection
            //2. add reference to newOutputConnection with the input connection
            //3. clear old oldOutputConnection connection references
            foreach (PinConnection inputNodeThatRefsOutput in oldOutputConnection.Refs)
            {
                Assert(inputNodeThatRefsOutput.Refs.Count <= 1 && inputNodeThatRefsOutput.InputOrBidirectional, "Output node references non input node");
                inputNodeThatRefsOutput.Refs.Clear();
                inputNodeThatRefsOutput.Refs.Add(newOutputConnection);
                newOutputConnection.Refs.Add(inputNodeThatRefsOutput);
            }
            oldOutputConnection.Refs.Clear();
            oldOutputConnection.Refs.Add(newInputConnection);
            newInputConnection.Refs.Add(oldOutputConnection);

            return pinNode;
        }


        /// <summary>
        /// Attempts to simplify the node\connection structure.
        /// </summary>
        void Simplify()
        {
            //Loop though each input connection in the module
            foreach (PinConnection aInput in _mod.Connections.Where(x => x.InputOrBidirectional))
            {
                //Skip if input has no references
                if (aInput.Refs.Count == 0)
                    continue;

                //Get the Node of the input connection
                Node a = aInput.Parent;

                //If a has multiple inputs, then we cannot remove all of a's inputs (cannot simply)
                if (a.Connections.GetInputs().Count() > 1)
                    continue;

                //Get the sole output connection (bInput) for this input connection (aInput)
                PinConnection bOutput = aInput.Refs[0];

                //Get the parent node b for bInput connection
                Node b = bOutput.Parent;

                //If a is a Pin and b in a PinNode
                //Since a PinNode is buried logic within a node structure and should only have 1 input, the result of some combinational logic, we can just
                //remove that node
                if (a.Type == NodeType.Pin && b.Type == NodeType.PinNode)
                {
                    //This will remove node PinNode, merge its input into a
                    RemoveAdjacentNode(aInput);
                }
            }

            List<Node> removeNodes = new List<Node>();
            foreach (Node node in _mod.Cells)
            {
                //For latches, we cannot support asynchronous clear or presets, make sure that
                //if they exist, they are set to a constant 0, and just remove them
                //TODO: Find a better place for this operation, or modify the yosys files to remove them from the json file
                if (node.Type == NodeType.Latch)
                {
                    List<PinConnection> removeCons = new List<PinConnection>();
                    foreach (PinConnection con in node.Connections.GetInputs())
                    {
                        switch (con.Name)
                        {
                            case "PRE":
                            case "CLR":
                                Node refNode = con.Refs[0].Parent;
                                Assert(refNode.Type == NodeType.Constant && refNode.Constant == 0, "Error, latch contains asynchronous clear or preset");
                                removeCons.Add(con);
                                removeNodes.Add(refNode);
                                break;
                        }
                    }
                    foreach (PinConnection con in removeCons)
                    {
                        node.Connections.Remove(con);
                    }
                }
            }
            foreach (Node node in removeNodes)
            {
                _mod.Cells.Remove(node);
            }
        }

        /// <summary>
        /// Removes the adjacent node that is referenced by aInput.  The inputs of the adjacent node is moved to the input connections parent node
        /// </summary>
        /// <param name="aInput">The input connection to the node we want to keep.  The node referencing the </param>
        static void RemoveAdjacentNode(PinConnection aInput)
        {
            Assert(aInput.InputOrBidirectional, "Cannot remove adjacent node of output node");

            Node a = aInput.Parent;

            //Get the sole output connection (bInput) for this input connection (aInput)
            PinConnection bOutput = aInput.Refs[0];

            //Get the parent node b for bInput connection
            Node b = bOutput.Parent;

            //Remove the bOutput PinConnection reference from aInput
            aInput.Refs.Clear();

            //Get the input connection for PinNode b (bInput) 
            PinConnection bInput = b.Connections.GetInputs().First();

            //Remove the aInput from the a PIN
            a.Connections.Remove(aInput);

            //Change the name of bInput to match old aInput
            bInput.Name = aInput.Name;

            //Add the bInput connection to a
            a.Connections.Add(bInput);

            //Change the parent node of bInput to a
            bInput.Parent = a;
            UpdateReplacementNode(a, bOutput);

            //Copy process state of b into a
            a.NodeProcessState |= b.NodeProcessState;

            //Clear all references held in bOutput 
            bOutput.Refs.Clear();

            //Clear all connections of b (its gone now, b's inputs was copyied into a)
            b.Connections.Clear();
        }

        /// <summary>
        /// Determines if a node feeds back onto itself
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        bool IsFeedbackNode(Node node)
        {
            List<Node> combinNodes = new List<Node>();
            List<Node> _ = new List<Node>();
            List<Node> terminalNodes = new List<Node>();
            foreach (PinConnection conIn in node.Connections.GetInputsOrBidirectional())
            {
                if (conIn.Refs.Count == 0)
                    continue;
                PinConnection conOutRef = conIn.Refs[0];
                ScanExpressionNodes(conOutRef, combinNodes, _, false, terminalNodes);
            }
            return terminalNodes.Contains(node);
        }

        int CalculateComplexity()
        {
            int ret = 0;
            _visited.Clear();
            foreach (PinConnection con in _mod.Connections)
            {
                if (!con.InputOrBidirectional)
                    continue;
                PinConnection refToInput = con.Refs.FirstOrDefault(r => r.DirectionType == DirectionType.Output);
                if (refToInput == null)
                    continue;
                _visited.Add(con.Parent);
                ret += con.Parent.Complexity = PopulateOutputComplexity(refToInput);
            }
            return ret;
        }

        /// <summary>
        /// Returns next PinNode that can be expanded.  Only combinational pinodes that have no feedback is considered.
        /// Returned node is chosen based on its output complexity (node with the smallest complexity is returned)
        /// </summary>
        /// <param name="limitCombin">Preferred limit of combinational pinnodes</param>
        /// <returns>null if no candidates are found</returns>
        Node GetNextCollapsedCombinLogicPinNode(int limitCombin)
        {
            if (limitCombin >= _createdPinNodes.Count)
                return null;

            CalculateComplexity();

            //Find combinational pinnodes (with no feedback)
            var comboPinNodes = _createdPinNodes.Where(x => x.NodeProcessState == NodeProcessState.None && false == IsFeedbackNode(x)).ToArray();
            if (comboPinNodes.Length < limitCombin)
            {
                return null;
            }
            //Sort the array based on OutputComplexity in descending order
            Array.Sort(comboPinNodes, (x, y) => { return x.OutputComplexity.CompareTo(y.OutputComplexity); });
            //Get the list of nodes to remove
            Node[] removePinNodes = comboPinNodes.Take(Math.Max(comboPinNodes.Length - limitCombin, 0)).ToArray();

            //return the top node, or null if none found
            return removePinNodes.Length == 0 ? null : removePinNodes[0];
        }

        /// <summary>
        /// Scans an output connection 
        /// </summary>
        /// <param name="outputConnection"></param>
        /// <param name="foundNodes"></param>
        /// <param name="dupNodes"></param>
        /// <param name="duplicate"></param>
        void ScanExpressionNodes(PinConnection outputConnection, List<Node> foundCombinNodes, List<Node> dupCombinNodes, bool dupCombin, List<Node> foundTerminalNodes)
        {
            Assert(outputConnection.OutputOrBidirectional, "Attempting to scan or duplicate with a non output connection");
            Node old = outputConnection.Parent;
            switch (old.Type)
            {
                case NodeType.Latch:
                case NodeType.DFF:
                case NodeType.TBUF:
                case NodeType.Pin:
                case NodeType.PinNode:
                case NodeType.Module:
                    foundTerminalNodes.Add(old);
                    return;
            }
            foundCombinNodes.Add(old);
            if (dupCombin)
            {
                Node dup = new Node(old.Name, old.Type, old.Constant);
                dup.Complexity = old.Complexity;
                dup.NodeProcessState = old.NodeProcessState;
                dupCombinNodes.Add(dup);
                foreach (PinConnection oldConnection in old.Connections)
                {
                    dup.Connections.Add(new PinConnection(dup, oldConnection.Name, oldConnection.DirectionType));
                }
            }
            foreach (var inputCon in old.Connections.GetInputs())
            {
                if (inputCon.Refs.Count == 0)
                {
                    continue;
                }
                ScanExpressionNodes(inputCon.Refs.First(), foundCombinNodes, dupCombinNodes, dupCombin, foundTerminalNodes);
            }
        }
         

        string ScrubPinName(string name)
        {
            StringBuilder sb = new StringBuilder(name);
            for(int ix = 0; ix < sb.Length; ++ix)
            {
                char c = sb[ix];
                if( !char.IsNumber(c) && !char.IsLetter(c))
                {
                    c = '_';
                }
                sb[ix] = c;
            }
            return sb.ToString();
        }

        void WriteHeader(TextWriter tr)
        {
            tr.Write("Name Name ;");
            tr.Write(ENDLINE);
            tr.Write("Partno 00 ;");
            tr.Write(ENDLINE);
            tr.Write($"Date {DateTime.Now.ToString("MMM yyyy")};");
            tr.Write(ENDLINE);
            tr.Write("Revision 01;");
            tr.Write(ENDLINE);
            tr.Write("Designer Engineer;");
            tr.Write(ENDLINE);
            tr.Write("Company None;");
            tr.Write(ENDLINE);
            tr.Write("Assembly None;");
            tr.Write(ENDLINE);
            tr.Write("Location ;");
            tr.Write(ENDLINE);
            tr.Write($"Device {_config.Device};"); //Example: f1508ispplcc84
            tr.Write(ENDLINE);
            WriteGroupSeparator(tr);
            string version = Assembly.GetAssembly(typeof(CodeGenCupl)).GetName().Version.ToString();
            tr.Write($"/* The following was auto-generated by JsonToCUPL {version} */");
        }

        void WriteGroupSeparator(TextWriter tr)
        {
            tr.Write(ENDLINE);
            tr.Write(ENDLINE);
            tr.Write(ENDLINE);
        }

        void WritePinDefinitions(TextWriter tr)
        {
            foreach (Node pin in _createdPins)
            {
                int pinNum = _config.PinNums[pin.Name];
                string sPinNum = pinNum == 0 ? "" : pinNum.ToString();
                tr.Write($"PIN  {sPinNum}  = " + pin.Name + ";");
                tr.Write(ENDLINE);
            }
        }

        void WritePinNodeDefinitions(TextWriter tr)
        {
            foreach (Node pinNode in _createdPinNodes)
            {
                tr.Write("PINNODE      = " + pinNode.Name + ";");
                tr.Write(ENDLINE);
            }
        }

        void WriteExpressions(TextWriter tr)
        {
            _visited.Clear();
            foreach (PinConnection con in _mod.Connections)
            {
                if (!con.InputOrBidirectional)
                    continue;
                PinConnection refToInput = con.Refs.FirstOrDefault(r => r.DirectionType == DirectionType.Output);
                if (refToInput == null)
                    continue;
                string name = con.Name;
                if (con.Parent.Type == NodeType.PinNode || con.Parent.Type == NodeType.Pin)
                {
                    if (con.Name != "_PIN_OUT" && con.Name != "_PIN_IN" && con.Name != con.Parent.Name)
                        name = con.Parent.Name + "." + con.Name;
                    else
                        name = con.Parent.Name;
                }
                else if (con.Parent.Type != NodeType.Module)
                {
                    name = con.Parent.Name + "." + con.Name;
                }
                _visited.Add(con.Parent);
                StringBuilder sb = new StringBuilder();
                sb.Append(name + " = ");
                GenerateComboLogic(refToInput, sb);
                sb.Append(";");
                //WinCupl is old, so just assume it has some issues with lines that are too long.
                string code = Util.Wrap(sb.ToString(), 80);
                tr.Write(code);
                tr.Write(ENDLINE);
            }
        }


        /// <summary>
        /// Recursively generates CUPL boolean expressions.  Walks though the node graph
        /// </summary>
        /// <param name="outputConnection">Current connection</param>
        /// <param name="sb">Output code StringBuffer</param>
        void GenerateComboLogic(PinConnection outputConnection, StringBuilder sb)
        {
            bool skip = false;
            Assert(outputConnection.OutputOrBidirectional, "Invalid connection processing point, connection not output or bidirectional");

            Node parentNode = outputConnection.Parent;
            if (_visited.Contains(parentNode))
            {
                skip = true;
            }
            _visited.Add(parentNode);
            switch (parentNode.Type)
            {
                case NodeType.Latch:
                case NodeType.DFF:
                case NodeType.TBUF:
                    sb.Append(parentNode.Name + "." + outputConnection.Name);
                    skip = true;
                    break;
                case NodeType.Pin:
                case NodeType.PinNode:
                    sb.Append(parentNode.Name);
                    skip = true;
                    break;
                case NodeType.Module:
                    sb.Append(outputConnection.Name);
                    skip = true;
                    break;
                case NodeType.Constant:
                    sb.Append("'b'" + parentNode.Constant);
                    skip = true;
                    break;
            }
            if (skip)
            {
                return;
            }
            if (parentNode.Type == NodeType.Not)
            {
                sb.Append("! ( ");
            }
            else
            {
                sb.Append(" ( ");
            }
            bool didWriteOperator = false;
            foreach (PinConnection con in parentNode.Connections)
            {
                if (!con.InputOrBidirectional)
                    continue;
                if (con.Refs.Count == 0)
                    continue;
                Assert(con.Refs.Count == 1, "Invalid input reference count at " + con.Name);

                //Recurse the next connection reference
                GenerateComboLogic(con.Refs[0], sb);
                if (!didWriteOperator)
                {
                    switch (parentNode.Type)
                    {
                        case NodeType.And:
                            sb.Append(" & ");
                            break;
                        case NodeType.Or:
                            sb.Append(" # ");
                            break;
                        case NodeType.Xor:
                            sb.Append(" $ ");
                            break;
                        default:
                            Assert(false, $"Unknown combinational operator type '{parentNode.Type}'");
                            break;
                        case NodeType.Not:
                            break;
                    }

                    didWriteOperator = true;
                }
            }
            sb.Append(" )");
        }

        /// <summary>
        /// Recurses though an output connection and returns its complexity
        /// </summary>
        /// <param name="outputConnection"></param>
        /// <returns></returns>
        int PopulateOutputComplexity(PinConnection outputConnection)
        {
            int ret = 1;
            Assert(outputConnection.OutputOrBidirectional,
                "Invalid connection processing point, connection not output or bidirectional");
            Node parentNode = outputConnection.Parent;
            bool skip = SkipRecurse(parentNode);
            if (skip)
            {
                return ret;
            }
            foreach (PinConnection con in parentNode.Connections)
            {
                if (!con.InputOrBidirectional)
                    continue;
                if (con.Refs.Count == 0)
                    continue;
                Assert(con.Refs.Count == 1, "Invalid input reference count at " + con.Name);
                //Recurse the next connection reference
                ret += PopulateOutputComplexity(con.Refs[0]);
            }
            parentNode.Complexity = ret;
            return ret;
        }

        bool SkipRecurse(Node parentNode)
        {
            bool skip = false;
            if (_visited.Contains(parentNode))
            {
                skip = true;
            }
            _visited.Add(parentNode);
            switch (parentNode.Type)
            {
                case NodeType.Latch:
                case NodeType.DFF:
                case NodeType.TBUF:
                case NodeType.Pin:
                case NodeType.PinNode:
                case NodeType.Module:
                case NodeType.Constant:
                    skip = true;
                    break;
            }

            return skip;
        }

        void AddPinNode(Node pinNode)
        {
            Assert(!_createdPinNodes.Contains(pinNode), $"Duplicate pinnode {pinNode.Name} added to created pinnode list");
            _createdPinNodes.Add(pinNode);
        }

        /// <summary>
        /// Adds a pin, a special node that is externally accessible 
        /// </summary>
        /// <param name="pin"></param>
        void AddPin(Node pin)
        {
            Assert(pin.Type == NodeType.Pin, "Adding node that is not a Pin");
            Assert(false == _createdPins.Contains(pin), "Pin node already added to pin collection");
            _createdPins.Add(pin);
        }

        /// <summary>
        /// For a given output connection, look for all referenced input connections and move them into the reference of the replaceNode's output connection
        /// 
        /// </summary>
        /// <param name="replaceNode"></param>
        /// <param name="output"></param>
        static void UpdateReplacementNode(Node replaceNode, PinConnection output)
        {
            Assert(output.OutputOrBidirectional, "Cannot Update a replacement node for a non output connection");

            PinConnection replaceNodeOutput = replaceNode.Connections.GetOutput();
            if (replaceNodeOutput == null)
            {
                replaceNodeOutput = new PinConnection(replaceNode, "_PIN_OUT", DirectionType.Output);
                replaceNode.Connections.Add(replaceNodeOutput);
            }

            foreach (PinConnection inputNodeToOutput in output.Refs)
            {
                if (inputNodeToOutput.Parent == replaceNode)
                    continue;

                inputNodeToOutput.Refs.Clear();
                inputNodeToOutput.Refs.Add(replaceNodeOutput);
                if (!replaceNodeOutput.Refs.Contains(inputNodeToOutput))
                {
                    replaceNodeOutput.Refs.Add(inputNodeToOutput);
                }
            }
        }
    }
}