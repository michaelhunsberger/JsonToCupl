using JsonToCupl;
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

        public void GenerateBranchingNodes()
        {
            CreatePins();
            CreateBranchingPinNodes();
            RebuildConnections();
            CheckConnections();
        }

        public void SimplifyConnections()
        {
            Simplify();
            RebuildConnections();
            CheckConnections();
        }

        public void GenerateCollapseNodes()
        {
            CollapseTriStateBuffers();
            RebuildConnections();
            CollapseRegisters();
            RebuildConnections();
        }

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
                if (output.Refs.Count == 0) continue;
                Node mergeTo = null;

                if (output.Refs.Count > 1)
                {
                    //If this node is outputted to multiple nodes, find a candidate pin 
                    Node[] foundPins = output.Refs.Select(r => r.Parent).Where(n => n.Type == NodeType.Pin).ToArray();
                    if (foundPins.Length == 1)
                    {
                        mergeTo = foundPins[0];
                    }
                    else
                    {
                        mergeTo = CreatePinNodeForOutput(output);
                        AddPinNode(mergeTo);
                    }
                }
                else
                {
                    mergeTo = output.Refs.First().Parent;
                    if (mergeTo.Type != NodeType.Pin && mergeTo.Type != NodeType.PinNode)
                    {
                        mergeTo = CreatePinNodeForOutput(output);
                        AddPinNode(mergeTo);
                    }
                }
                Trace.Assert((mergeTo.NodeProcessState & NodeProcessState.MergeTBUF) == 0, $"Node {mergeTo.Name} already contains TBUF inputs");
                //Input connection to the mergeTo Node
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
                        //we are merging to instead of just adding the connection
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
		
        void CollapseRegisters()
        {
            foreach (Node node in _mod.Cells.Where(c => c.Type.IsDFFOrLatch()))
            {
                PinConnection output = node.Connections.GetOutput();
                if (output.Refs.Count == 0) continue;
                Node mergeTo = null;

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

                PinConnection mergeToInput = null;
                //TODO: Consider moving this code into the search for mergeTo candidate part at the top of this function
                //... if (output.Refs.Count > 1) ...
                //If mergeTo was already merged as a DFF, do not merge another DFF to this pin\pinnode
                if ((mergeTo.NodeProcessState & NodeProcessState.MergeDFF) != 0)
                {
                    mergeTo = CreatePinNodeForOutput(output);
                    AddPinNode(mergeTo);
                    mergeToInput = mergeTo.Connections.GetInputs().First();
                }
                //We can only merge into a node processed as a tbuf if the DFF output goes to the TBUF input (not the output enable)
                //WARNING! Consider only doing this if the dff is only outputted to the tbuf and not other nodes
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

                Trace.Assert(mergeToInput != null, "Unable to find an input connection within the merge node");

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

        void CreateBranchingPinNodes()
        {
            CreateBranchingPinNodes(_mod);
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
            foreach (PinConnection aInput in _mod.Connections.Where(x => x.InputOrBidirectional))
            {
                if (aInput.Refs.Count == 0)
                    continue;
                Node a = aInput.Parent;
                //If A has multiple inputs, then we cannot remove all of A's inputs
                if (a.Connections.GetInputs().Count() > 1)
                    continue;
                PinConnection bOutput = aInput.Refs[0];
                Node b = bOutput.Parent;
                //If A(PinNode|Pin) = B(PinNode)
                if (a.Type == NodeType.Pin && b.Type == NodeType.PinNode)
                {
                    PinConnection bInput = b.Connections.GetInputs().First();
                    aInput.Refs.Clear();
                    a.Connections.Remove(aInput);
                    a.Connections.Add(bInput);
                    bInput.Name = aInput.Name;
                    bInput.Parent = a;
                    UpdateReplacementNode(a, bOutput);
                    a.NodeProcessState |= b.NodeProcessState;
                    bOutput.Refs.Clear();
                    b.Connections.Clear();
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

        void AddPin(Node pin)
        {
            if (pin.Type != NodeType.Pin)
                throw new ArgumentException();
            if (_createdPins.Contains(pin))
                throw new ApplicationException($"Duplicate pin {pin.Name} added to created pin list");
            _createdPins.Add(pin);
        }

        static void UpdateReplacementNode(Node replaceNode, PinConnection output)
        {
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