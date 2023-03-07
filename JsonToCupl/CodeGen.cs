﻿using JsonToCupl;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Reflection;
using System.Text;

namespace JsonToCupl
{
    class CodeGen
    {
        readonly ContainerNode _mod;
        readonly HashSet<Node> _visited = new HashSet<Node>();
        readonly HashSet<Node> _createdPinNodes = new HashSet<Node>();
        readonly HashSet<Node> _createdPins = new HashSet<Node>();
        readonly IConfig _config;

        //Explicitly use \r\n instead of using the Environment.NewLine.
        const string ENDLINE = "\r\n";

        public CodeGen(ContainerNode mod, IConfig config)
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
        public void WriteCUPL(TextWriter tr)
        {
            WriteHeader(tr);
            WriteGroupSeparator(tr);
            WritePinDefinitions(tr);
            WriteGroupSeparator(tr);
            WritePinNodeDefinitions(tr);
            WriteGroupSeparator(tr);
            WriteExpressions(tr);
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
		
        void RebuildConnections()
        {
            _mod.Connections.Clear();
            foreach (Node cell in _createdPins)
            {
                CheckNode(cell);
                AddInputsToModuleConnection(cell);
            }
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
        /// Loops though each nodes input connections, if there are no referencess to that input internally within the node graph, then it must come externally
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
        /// If TBUF's output is referenced by more than one node, it attempts to find a sutible
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
                Trace.Assert((mergeTo.NodeProcessState & NodeProcessState.MergeTBUF) == 0, $"Node {mergeTo.Name} already contains TBUF inputs");
                PinConnection[] inputsToMergeTo = mergeTo.Connections.GetInputsOrBidirectional().ToArray();
                Trace.Assert(inputsToMergeTo.Length == 1, "mergeTo node contains multiple inputs");
                PinConnection mergeToInput = inputsToMergeTo[0];
                Trace.Assert(mergeToInput.Refs.Contains(output), "Top level node does not contain TBUF output");
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
                if ((mergeTo.NodeProcessState & NodeProcessState.MergeDFF) != 0)
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
                    Trace.Assert(mergeToInputs.Length == 1, "Inconsistent number of inputs in merge to node");
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

                mergeTo.NodeProcessState |= NodeProcessState.MergeTBUF;

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

        void CheckNode(Node node)
        {
            PinConnection output = node.Connections.GetOutput();
            if (output != null)
            {
                foreach (var inputRefOutput in output.Refs)
                {
                    Trace.Assert(inputRefOutput.InputOrBidirectional, "Non input connection referenced by output");
                    Trace.Assert(inputRefOutput.Refs.Contains(output), "Input connection does not reference required output connection");
                }
            }

            foreach (var input in node.Connections.GetInputsOrBidirectional())
            {
                foreach (var outputRefInput in input.Refs)
                {
                    Trace.Assert(outputRefInput.DirectionType == DirectionType.Output, "Non output connection referenced by input");
                    Trace.Assert(outputRefInput.Refs.Contains(input), "Output connection does not reference required input connection");
                }
            }
        }

     

        /// <summary>
        /// Recursively walks though all input connections of a node
        /// If the corresponding output connection references more than one node, then an
        /// a pinnode is created.
        ///
        /// This is done so repeated combinational logic is generated per CUPL expression.
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
                Trace.Assert(connection.Refs.Count == 1, "Invalid number of connections to input node " + connection.Name);
                PinConnection output = connection.Refs[0];
                Trace.Assert(output.OutputOrBidirectional == true, "Unknown connection to input node " + connection.Name);
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
            Trace.Assert(oldOutputConnection.OutputOrBidirectional, $"Cannot create PinNode on node {oldOutputConnection.Parent.Name}.{oldOutputConnection.Name}");
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
                if (inputNodeThatRefsOutput.Refs.Count > 1 || false == inputNodeThatRefsOutput.InputOrBidirectional)
                {
                    throw new ApplicationException("Output node references non input node");
                }

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
                //Skip if input has no referecens
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
                if (node.Type == NodeType.Latch)
                {
                    List<PinConnection> removeCons = new List<PinConnection>();
                    foreach(PinConnection con in node.Connections.GetInputs())
                    {
                        switch(con.Name)
                        {
                            case "PRE":
                            case "CLR":
                                Node refNode = con.Refs[0].Parent;
                                if(refNode.Type != NodeType.Constant || refNode.Constant != 0)
                                {
                                    throw new NotSupportedException("Error, latch contains asynchronous clear or preset");
                                }
                                removeCons.Add(con);
                                removeNodes.Add(refNode);
                                break;
                        }
                    }
                    foreach(PinConnection con in removeCons)
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
            Trace.Assert(aInput.InputOrBidirectional, "Cannot remove adjacent node of output node");

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
            string version = Assembly.GetAssembly(typeof(CodeGen)).GetName().Version.ToString();
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
                if (!con.InputOrBidirectional) continue;
                PinConnection refToInput = con.Refs.FirstOrDefault(r => r.DirectionType == DirectionType.Output);
                if (refToInput == null) continue;
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
                string code = Util.Wrap(sb.ToString(), 80);
                //WinCupl is old, so just assume it has some issues with lines that are too long.
                tr.Write(code);
                tr.Write(ENDLINE);
                Console.WriteLine(code);
            }
        }

        void GenerateComboLogic(PinConnection outputConnection, StringBuilder sb)
        {
            bool skip = false;
            Trace.Assert(outputConnection.OutputOrBidirectional,
                "Invalid connection processing point, connection not output or bidirectional");
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
            if (skip) return;

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
                Trace.Assert(con.Refs.Count == 1, "Invalid input reference count at " + con.Name);
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
                            Trace.Assert(false, "Unknown combinational operator type '{parentNode.Type}'");
                            break;
                        case NodeType.Not:
                            break;
                    }

                    didWriteOperator = true;
                }
            }
            sb.Append(" )");
        }

        void AddPinNode(Node pinNode)
        {
            if (pinNode == null)
                throw new ArgumentNullException(nameof(pinNode));
            if (pinNode.Type != NodeType.PinNode)
                throw new ArgumentException();
            if (_createdPinNodes.Contains(pinNode))
                throw new ApplicationException($"Duplicate pinnode {pinNode.Name} added to created pinnode list");
            _createdPinNodes.Add(pinNode);
        }

        /// <summary>
        /// Adds a pin, a special node that is externally accessible 
        /// </summary>
        /// <param name="pin"></param>
        void AddPin(Node pin)
        {
            Trace.Assert(pin.Type == NodeType.Pin, "Adding node that is not a Pin");
            Trace.Assert(false == _createdPins.Contains(pin), "Pin node already added to pin collection");
            _createdPins.Add(pin);
        }

        /// <summary>
        /// For a given output connection, look for all referenced input connections and move them into the referene of the replaceNode's output connection
        /// 
        /// </summary>
        /// <param name="replaceNode"></param>
        /// <param name="output"></param>
        static void UpdateReplacementNode(Node replaceNode, PinConnection output)
        {
            Trace.Assert(output.OutputOrBidirectional, "Cannot Update a replacement node for a non output connection");

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