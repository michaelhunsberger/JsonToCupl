using JsonToCupl;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Text;

namespace JsonToCupl
{
    class CodeGen
    {
        readonly ContainerNode _mod;
        readonly HashSet<Node> _visited = new HashSet<Node>();
        readonly HashSet<Node> _createdPinNodes = new HashSet<Node>();
        readonly HashSet<Node> _createdPins = new HashSet<Node>();

        //Explicitly use \r\n instead of using the Environment.NewLine 
        const string ENDLINE = "\r\n";

        public CodeGen(ContainerNode mod)
        {
            _mod = mod;
        }

        public void Generate(TextWriter tr)
        {
            CreatePins();
            CreateBranchingPinNodes();
            RebuildConnections();

            Simplify();
            RebuildConnections();

            CollapseTBUF();
            RebuildConnections();
            CollapseDFF();
            RebuildConnections();

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
            foreach (PinConnection con in _mod.Connections)
            {
                if (con.Parent.Type == NodeType.Module)
                {
                    Node pin = con.Parent = new Node(con.Name, NodeType.Pin);
                    pin.Connections.Add(con);
                    AddPin(pin);
                }
            }
        }
        void RebuildConnections()
        {
            _mod.Connections.Clear();
            foreach (Node cell in _createdPins)
            {
                switch (cell.Type)
                {
                    case NodeType.Pin:
                        AddInputsToModuleConnection(cell);
                        break;
                }
            }

            foreach (Node cell in _createdPinNodes)
            {
                switch (cell.Type)
                {
                    case NodeType.PinNode:
                        AddInputsToModuleConnection(cell);
                        break;
                }
            }

            foreach (Node cell in _mod.Cells)
            {
                switch (cell.Type)
                {
                    case NodeType.DFF:
                    case NodeType.TBUF:
                        AddInputsToModuleConnection(cell);
                        break;
                }
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

        void CollapseTBUF()
        {
            foreach (Node node in _mod.Cells)
            {
                if (node.Type != NodeType.TBUF) continue;
                PinConnection output = node.Connections.GetOutput();
                if (output.Refs.Count == 0) continue;
                Node mergeTo = null;

                if (output.Refs.Count > 1)
                {
                    //If this node is outputed to multiple nodes, find a candidate. 
                    Node[] foundPins = output.Refs.Select(r => r.Parent).Where(n => n.Type == NodeType.Pin).ToArray();
                    if (foundPins.Length == 1)
                    {
                        //If this DFF outputs a value to more than one node, if where is one node that is a pin, consider merging this dff to that pin
                        mergeTo = foundPins[0];
                    }
                    else
                    {
                        mergeTo = CreatePinNodeForOutput(output, false);
                        AddPinNode(mergeTo);
                    }
                }
                else
                {
                    mergeTo = output.Refs.First().Parent;
                    if (mergeTo.Type != NodeType.Pin && mergeTo.Type != NodeType.PinNode)
                    {
                        mergeTo = CreatePinNodeForOutput(output, false);
                        AddPinNode(mergeTo);
                    }
                }
                if ((mergeTo.NodeProcessState & NodeProcessState.MergeTBUF) != 0)
                {
                    throw new ApplicationException($"Node {mergeTo.Name} already contains TBUF inputs");
                }

                //Input connection to the mergeTo Node
                PinConnection mergeToInput = mergeTo.Connections.GetInputsOrBidirectional().First();
                if (!mergeToInput.Refs.Contains(output))
                    throw new ApplicationException("Top level node does not contain TBUF output");
                mergeToInput.Refs.Clear();

                foreach (PinConnection inputsToMerge in node.Connections.GetInputs())
                {
                    PinConnection outputToInputToMerge = inputsToMerge.Refs.First();
                    if (inputsToMerge.Name == "A")
                    {
                        //inputsToMerge is the value part of the TBUF, attach the referenced output to the input pin we are merging to
                        //instead of just adding the connection
                        mergeToInput.Refs.Add(outputToInputToMerge);
                        outputToInputToMerge.Refs.Remove(inputsToMerge);
                        outputToInputToMerge.Refs.Add(mergeToInput);
                    }
                    else
                    {
                        mergeTo.Connections.Add(inputsToMerge);
                    }
                    inputsToMerge.Parent = mergeTo;
                }

                UpdateReplacementNode(mergeTo, output);

                mergeTo.NodeProcessState |= NodeProcessState.MergeTBUF;
                output.Refs.Clear();
                node.Connections.Clear();
            }
        }

        void CollapseDFF()
        {
            foreach (Node node in _mod.Cells)
            {
                if (node.Type != NodeType.DFF) continue;
                PinConnection output = node.Connections.GetOutput();
                if (output.Refs.Count == 0) continue;
                Node mergeTo = null;

                //If this dff references more than one node, find a node candidate that we can merge to
                if (output.Refs.Count > 1)
                {
                    //Look for a PIN
                    Node[] foundPins = output.Refs.Select(r => r.Parent).Where(n => n.Type == NodeType.Pin).ToArray();
                    if (foundPins.Length == 1)
                    {
                        //If this DFF outputs a value to more than one node, if where is one node that is a pin, consider merging this dff to that pin
                        mergeTo = foundPins[0];
                    }
                    else
                    {
                        //We reference to more than one node, so create a pinnode and merge into that
                        mergeTo = CreatePinNodeForOutput(output, false);
                        AddPinNode(mergeTo);
                    }
                }
                else
                {
                    mergeTo = output.Refs[0].Parent;
                    if (mergeTo.Type != NodeType.Pin && mergeTo.Type != NodeType.PinNode)
                    {
                        mergeTo = CreatePinNodeForOutput(output, false);
                        AddPinNode(mergeTo);
                    }
                }

                PinConnection mergeToInput = null;
                //TODO: Consider moving this code into the search for mergeTo candidate part at the top of this function ... if (output.Refs.Count > 1) ...
                //If mergeTo was already merged as a DFF, there is no way we can merge another DFF to this pin\pinnode
                if ((mergeTo.NodeProcessState & NodeProcessState.MergeTBUF) != 0)
                {
                    mergeTo = CreatePinNodeForOutput(output, false);
                    AddPinNode(mergeTo);
                    mergeToInput = mergeTo.Connections.GetInputs().First();
                }
                //We can only merge into a node processed as a tbuf if the DFF output goes to the TBUF value input (not the enable)
                else if ((mergeTo.NodeProcessState & NodeProcessState.MergeTBUF) != 0)
                {
                    mergeToInput = mergeTo.Connections.Find(c => c.Refs.Contains(output));
                    if (mergeToInput.Name != "A")
                    {
                        mergeTo = CreatePinNodeForOutput(output, false);
                        AddPinNode(mergeTo);
                        mergeToInput = mergeTo.Connections.GetInputs().First();
                    }
                }
                //mergeTo is just a plain old PIN\PINODE, we can merge into this node.
                else
                {
                    //Check to make sure there are not multiple inputs
                    PinConnection[] mergeToInputs = mergeTo.Connections.GetInputs().ToArray();
                    if (mergeToInputs.Length != 1)
                        throw new ApplicationException("Inconsistent number of inputs in merge to node");
                    mergeToInput = mergeToInputs[0];
                }

                if (mergeToInput == null)
                    throw new ApplicationException("Unable to find an input connection within the merge node");

                //Clear this input reference (which is the output of node)
                mergeToInput.Refs.Clear();

                //Remove this input from mergeTo
                mergeTo.Connections.Remove(mergeToInput);

                /*
                 * We need to merge the DFF(node) inputs into mergeTo
                 * All values that reference output nee
                 *
                 */
                foreach (PinConnection inputsToMerge in node.Connections.GetInputs())
                {
                    mergeTo.Connections.Add(inputsToMerge);
                    inputsToMerge.Parent = mergeTo;
                }

                UpdateReplacementNode(mergeTo, output);

                mergeTo.NodeProcessState |= NodeProcessState.MergeTBUF;
                output.Refs.Clear();
                node.Connections.Clear();
            }
        }

        void CreateBranchingPinNodes()
        {
            CreateBranchingPinNodes(_mod);
        }

        void CreateBranchingPinNodes(Node node)
        {
            if (node.Type == NodeType.PinNode || _visited.Contains(node)) return;
            _visited.Add(node);
            foreach (PinConnection connection in node.Connections.Where(c => c.InputOrBidirectional))
            {
                if (connection.Refs.Count == 0) continue;
                if (connection.Refs.Count != 1)
                {
                    throw new ApplicationException("Invalid number of connections to input node " + connection.Name);
                }

                PinConnection output = connection.Refs[0];
                if (output.DirectionType != DirectionType.Output && output.DirectionType != DirectionType.Bidirectional)
                {
                    throw new ApplicationException("Unknown connection to input node " + connection.Name);
                }
                Node parentOutputNode = output.Parent;
                if (parentOutputNode.Type != NodeType.PinNode &&
                    parentOutputNode.Type != NodeType.DFF &&
                    parentOutputNode.Type != NodeType.TBUF &&
                    parentOutputNode.Type != NodeType.Pin &&
                    output.Refs.Count > 1)
                {
                    Node pinNode = CreatePinNodeForOutput(output, false);
                    AddPinNode(pinNode);
                }

                CreateBranchingPinNodes(parentOutputNode);
            }
        }

        Node CreatePinNodeForOutput(PinConnection output, bool replace)
        {
            if (!output.OutputOrBidirectional)
                throw new ApplicationException($"Cannot create PinNode on node {output.Parent.Name}.{output.Name}");

            string newName = output.Parent.Type == NodeType.DFF ? output.Parent.Name : Util.GenerateName();
            Node pinNode = new Node(newName, NodeType.PinNode);
            PinConnection outputForPinNode = new PinConnection(pinNode, "_PIN_OUT", DirectionType.Output);
            PinConnection inputForPinNode = new PinConnection(pinNode, "_PIN_IN", DirectionType.Input);
            pinNode.Connections.Add(outputForPinNode);
            pinNode.Connections.Add(inputForPinNode);
            InsertConnection(output, outputForPinNode, inputForPinNode, replace);
            return pinNode;
        }

        static void InsertConnection(PinConnection oldOutputConnection, PinConnection newOutputConnection,
           PinConnection newInputConnection, bool replace = false)
        {
            if (!(oldOutputConnection.OutputOrBidirectional && newOutputConnection.OutputOrBidirectional &&
                  newInputConnection.InputOrBidirectional))
            {
                throw new ArgumentException();
            }

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

            if (!replace)
            {
                oldOutputConnection.Refs.Clear();
                oldOutputConnection.Refs.Add(newInputConnection);
                newInputConnection.Refs.Add(oldOutputConnection);
            }
        }

        void Simplify()
        {
            List<PinConnection> listToRemove = new List<PinConnection>();
            do
            {
                listToRemove.Clear();
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
                    if ((a.Type == NodeType.Pin || a.Type == NodeType.PinNode) && b.Type == NodeType.PinNode)
                    {
                        //Remove aInput reference (which is bOutput)
                        aInput.Refs.Clear();
                        //Remove bOutput reference since it is going to be merged into A
                        bOutput.Refs.Clear();
                        //Remove aInput from the _mod.Connections list
                        listToRemove.Add(aInput);
                        //Remove aInput connection, all of B's inputs will be merged into A
                        a.Connections.Remove(aInput);

                        //Merge each input from B into A, change each input that references A to B output
                        foreach (PinConnection bInput in b.Connections.GetInputs())
                        {
                            PinConnection outputToBInput = bInput.Refs.First();
                            a.Connections.Add(bInput);
                            bInput.Parent = a;
                        }
                        a.NodeProcessState |= b.NodeProcessState;
                    }
                    //If A(PinNode) = B(Pin), and A is a PinNode and B is a Pin, 
                    else if (a.Type == NodeType.PinNode && b.Type == NodeType.Pin)
                    {
                        bOutput.Refs.Remove(aInput);
                        PinConnection aOutput = a.Connections.Find(x => x.DirectionType == DirectionType.Output);
                        foreach (PinConnection cInput in aOutput.Refs)
                        {
                            cInput.Refs.Remove(aOutput);
                            cInput.Refs.Add(bOutput);
                            bOutput.Refs.Add(cInput);
                        }
                        aOutput.Refs.Clear();
                        aInput.Refs.Clear();
                        listToRemove.Add(aInput);
                        a.NodeProcessState |= b.NodeProcessState;
                    }
                }
                foreach (PinConnection removeMe in listToRemove)
                {
                    _mod.Connections.Remove(removeMe);
                }
            }
            while (listToRemove.Count > 0);
        }

        void WriteHeader(TextWriter tr)
        { 
            tr.Write("Name changeme;");
            tr.Write(ENDLINE);
            tr.Write("Partno changeme;");
            tr.Write(ENDLINE);
            tr.Write($"Date {DateTime.Now.ToString("MMM yyyy")};");
            tr.Write(ENDLINE);
            tr.Write("Revision changeme;");
            tr.Write(ENDLINE);
            tr.Write("Designer changeme;");
            tr.Write(ENDLINE);
            tr.Write("Company changeme;");
            tr.Write(ENDLINE);
            tr.Write("Assembly changeme;");
            tr.Write(ENDLINE);
            tr.Write("Location changeme;");
            tr.Write(ENDLINE);
            tr.Write("Device virtual;");
            tr.Write(ENDLINE);
            tr.Flush();
        }

        void WriteGroupSeparator(TextWriter tr)
        {
            tr.Write(ENDLINE);
            tr.Write(ENDLINE);
            tr.Write(ENDLINE);
            tr.Write(ENDLINE);
        }

        void WritePinDefinitions(TextWriter tr)
        {
            foreach (Node pin in _createdPins)
            {
                tr.Write("Pin      = " + pin.Name);
                tr.Write("\r\n"); //Explicitly write \r\n, WinCupl requires line feed carriage return
            }
            tr.Flush();
        }

        void WritePinNodeDefinitions(TextWriter tr)
        {
            foreach (Node pinNode in _createdPinNodes)
            {
                tr.Write("PINNODE      = " + pinNode.Name);
                tr.Write("\r\n"); //Explicitly write \r\n, WinCupl requires line feed carriage return
            }
            tr.Flush();
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

                tr.Write(Util.Wrap(sb.ToString(), 80));
                tr.Write("\r\n");
                tr.Flush();
            }
        }

        void GenerateComboLogic(PinConnection outputConnection, StringBuilder sb)
        {
            bool skip = false;
            if (!outputConnection.OutputOrBidirectional)
            {
                throw new ApplicationException("Invalid connection processing point, node not an output or bidirectional");
            }
            Node parentNode = outputConnection.Parent;
            NodeType type = parentNode.Type;
            NodeType nodeType = type;
            //During development, I had to recover this function by disassembly.  Use proper switch statement  
            if (nodeType == NodeType.Module || (uint)(nodeType - 6) <= 4u)
            {
                skip = true;
            }
            if (_visited.Contains(parentNode))
            {
                skip = true;
            }
            _visited.Add(parentNode);

            switch (parentNode.Type)
            {
                case NodeType.DFF:
                case NodeType.TBUF:
                    sb.Append(parentNode.Name + "." + outputConnection.Name);
                    break;
                case NodeType.Pin:
                case NodeType.PinNode:
                    sb.Append(parentNode.Name);
                    break;
                case NodeType.Module:
                    sb.Append(outputConnection.Name);
                    break;
                case NodeType.Constant:
                    sb.Append("'b'" + parentNode.Constant);
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
                if (con.Refs.Count != 1)
                {
                    throw new ApplicationException("Invalid input reference count at " + con.Name);
                }
                GenerateComboLogic(con.Refs[0], sb);
                if (didWriteOperator) continue;
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
                        throw new ApplicationException($"Unknown combinational operator type '{parentNode.Type}'");
                    case NodeType.Not:
                        break;
                }
                didWriteOperator = true;
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
            if (pin == null)
                throw new ArgumentNullException(nameof(pin));
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