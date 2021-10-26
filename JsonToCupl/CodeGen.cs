using JsonToCupl;
using System;
using System.Collections.Generic;
using System.Linq;

namespace JsonToCupl
{
    class CodeGen
    {
        private enum CollapseNodeState
        {
            Start,
            FoundTriBuf,
            FoundRegister
        }

        private readonly ContainerNode _mod;

        private readonly HashSet<Node> _visited = new HashSet<Node>();

        private readonly List<Node> _createdPinNodes = new List<Node>();

        private readonly List<Node> _createdPins = new List<Node>();

        private readonly List<Node> _collapseNodes = new List<Node>();

        private CollapseNodeState _collapseNodeState = CollapseNodeState.Start;

        public CodeGen(ContainerNode mod)
        {
            _mod = mod;
        }

        public void Test()
        {
            foreach (PinConnection connection in _mod.Connections)
            {
                if (connection.Parent.Type == NodeType.Module)
                {
                    Node pin = connection.Parent = new Node(connection.Name, NodeType.Pin);
                    pin.Connections.Add(connection);
                    _createdPins.Add(pin);
                }
            }
            PrepareSyncronousNodes();
            CreatePinNodes(_mod);
            foreach (Node pinNode in _createdPinNodes)
            {
                foreach (PinConnection pinNodeCon in pinNode.Connections)
                {
                    if (pinNodeCon.DirectionType == DirectionType.Input)
                    {
                        _mod.Connections.Add(pinNodeCon);
                    }
                }
            }
            Symplify();
            CollapseNodes();
            GenerateExpressions();
            /*
            foreach (Node cell in _mod.Cells)
            {
                if (!_visited.Contains(cell))
                {
                    throw new ApplicationException("Unvisited cell " + cell.Name);
                }
            }
            */
        }

        void CollapseNodes()
        {
            foreach (PinConnection con in _mod.Connections)
            {
                Node topNode = con.Parent;
                if (con.InputOrBidirectional && (topNode.Type == NodeType.Pin || topNode.Type == NodeType.PinNode))
                {
                    if (con.Refs.Count == 1)
                    {
                        _visited.Clear();
                        _visited.Add(topNode);
                        _collapseNodes.Clear();
                        _collapseNodeState = CollapseNodeState.Start;
                        PinConnection outConnection = con.Refs[0];
                        CollapseNodes(outConnection.Parent);
                        ProcessCollapseNodes(topNode);
                    }
                }
            }
        }

        void ProcessCollapseNodes(Node topNode)
        {
            if (_collapseNodes.Count == 0)
                return;

            //Get the topMode's output connection, if it does not exist, create one
            PinConnection topNodeOutput = topNode.Connections.FirstOrDefault(c => c.OutputOrBidirectional);
            if (topNodeOutput == null)
            {
                topNodeOutput = new PinConnection(topNode, "_OUT", DirectionType.Output);
                topNode.Connections.Add(topNodeOutput);
            }

            //List of all inputs of syncronous nodes to move into the topNode
            var inputsToMove = new List<PinConnection>();
            //Found DFF node
            Node foundQNode = null;
            //Fond output to DFF node 
            PinConnection foundQNodeOutput = null;

            //Fill the inputsToMove list
            foreach (Node node in _collapseNodes)
            {
                //only consider DFF and TBUF
                if (node.Type == NodeType.DFF || node.Type == NodeType.TBUF)
                {
                    if (node.Type == NodeType.DFF)
                    {
                        if (foundQNode != null)
                            throw new ApplicationException("More than one registered output nodes found in collapse list");
                        foundQNode = node;
                    }
                    foreach (PinConnection con in node.Connections)
                    {
                        if (con.DirectionType == DirectionType.Input)
                        {
                            inputsToMove.Add(con);
                        }
                        else if (con.DirectionType == DirectionType.Output)
                        {
                            if (node.Type == NodeType.DFF && con.Name == "Q")
                            {
                                if (foundQNodeOutput != null)
                                    throw new ApplicationException("More than one registered output node connections found in collapse list");
                                foundQNodeOutput = con;
                            }
                        }
                    }
                }
            }

            //We need to consider outputs from pinnodes within the collapse list.
            //If they reference a collapsed DFF, all outputs to nodes not within the collapsed list will need be 
            //changed to the topNode
            List<PinConnection> inputRefsToDFFQ = new List<PinConnection>();
            foreach (Node node in _collapseNodes.Where(c => c.Type == NodeType.PinNode))
            {
                var output = node.Connections.FirstOrDefault(c => c.DirectionType == DirectionType.Output);
                var input = node.Connections.FirstOrDefault(c => c.DirectionType == DirectionType.Input);

                if (output != null)
                {
                    //Only consider nodes that have an input of DFF and is contained in the collapse list
                    bool consider = false;
                    //input.Refs.FirstOrDefault(rf => rf.Parent == foundQNode) != null;
                    foreach (var rf in input.Refs)
                    {
                        if (rf.Parent == foundQNode)
                        {
                            consider = true;
                            break;
                        }
                    }
                    //Save output connections only if the output references a pending collapsed syncronous node 
                    if (consider)
                    {
                        //Exclude any connection that points to a node in the existing list
                        var outConToNotInCollapsedList = output.Refs.FindAll(c => c.Parent != topNode && !_collapseNodes.Contains(c.Parent));
                        inputRefsToDFFQ.AddRange(outConToNotInCollapsedList);
                    }
                }
            }

            if (inputsToMove.Count > 0)
            {
                //Remove references from input node and references to this node
                //for any output node that is contained in the collapse list
                var inputToTopNode = topNode.Connections.First(c => c.InputOrBidirectional);
                var inputRefsToRemove = new List<PinConnection>();
                foreach (var refCon in inputToTopNode.Refs)
                {
                    if (refCon.OutputOrBidirectional)
                    {
                        if (_collapseNodes.Contains(refCon.Parent))
                        {
                            inputRefsToRemove.Add(refCon);
                            refCon.Refs.Remove(inputToTopNode);
                        }
                    }
                }
                foreach (var refCon in inputRefsToRemove)
                    inputToTopNode.Refs.Remove(refCon);

                //Move input connections to the topNode
                foreach (var mv in inputsToMove)
                {
                    bool performMove = false;
                    switch (mv.Parent.Type)
                    {
                        case NodeType.DFF:
                            performMove = true;
                            break;
                        case NodeType.TBUF:
                            if (mv.Name == "E")
                                performMove = true;
                            else if (mv.Name == "A")
                            {
                                //We only include the 'A' connection if it does not reference the foundQNodeOutput
                                //Walk though the pinnode chain and see if it references the foundQNodeOutput
                                //Restrict nodes to ones contained in the collapse list
                                performMove = true;
                                for (PinConnection cur = mv;
                                    cur != null && _collapseNodes.Contains(cur.Parent);
                                    cur = NextPinNodeInput(cur))
                                {
                                    if (cur.Refs.Contains(foundQNodeOutput))
                                    {
                                        performMove = false;
                                        break;
                                    }
                                }
                            }
                            break;
                    }
                    if (performMove)
                    {
                        mv.Parent.Connections.Remove(mv);
                        mv.Parent = topNode;
                        topNode.Connections.Add(mv);
                    }
                    else
                    {
                        //If we are not moving this input, clear its references
                        mv.Refs.Clear();
                    }
                }

                //For pinnodes that reference reference the DFF value, we need to make the topNode the output
                //and remove references to the pinnode
                foreach (var mv in inputRefsToDFFQ)
                {
                    mv.Refs.Clear();
                    mv.Refs.Add(topNodeOutput);
                    topNodeOutput.Refs.Add(mv);
                }

                //For each connection that was not moved, clear its refereces   
                foreach (var collapsed in _collapseNodes)
                {
                    PinConnection output = collapsed.Connections.GetOutput();
                    if (!IsReferencedInput(topNode, output))
                    {
                        foreach (var con in collapsed.Connections)
                            con.Refs.Clear();
                    }
                }
            }
        }

        bool IsReferencedInput(Node topNode, PinConnection con)
        {
            if (con.DirectionType == DirectionType.Output)
            {
                foreach (var input in topNode.Connections.GetInputs())
                {
                    if (input.Refs.Count == 1)
                    {
                        var inputRef = input.Refs[0];
                        if (inputRef == con)
                        {
                            return true;
                        }
                        else
                        {
                            Node nextParent = inputRef.Parent;
                            if (_collapseNodes.Contains(nextParent))
                                return IsReferencedInput(nextParent, con);
                        }
                    }
                }
            }
            return false;
        }

        PinConnection NextPinNodeInput(PinConnection con)
        {
            PinConnection ret = null;
            var parent = con.Refs[0].Parent;
            if (parent.Type == NodeType.PinNode)
            {
                ret = parent.Connections.GetInputs().First();
            }
            return ret;
        }

        void CollapseNodes(Node node)
        {
            if (_visited.Contains(node))
            {
                return;
            }
            _visited.Add(node);
            bool walk = false;
            switch (node.Type)
            {
                case NodeType.DFF:
                    if (_collapseNodeState != CollapseNodeState.FoundRegister)
                    {
                        walk = true;
                        _collapseNodeState = CollapseNodeState.FoundRegister;
                    }
                    break;
                case NodeType.TBUF:
                    if (_collapseNodeState == CollapseNodeState.Start)
                    {
                        walk = true;
                        _collapseNodeState = CollapseNodeState.FoundTriBuf;
                    }
                    break;
                case NodeType.PinNode:
                    walk = true;
                    break;
            }
            if (walk)
            {
                _collapseNodes.Add(node);
                if (_collapseNodeState != CollapseNodeState.FoundRegister)
                {
                    foreach (PinConnection inputConnection in node.Connections.Where((PinConnection x) => x.DirectionType == DirectionType.Input))
                    {
                        foreach (PinConnection outConnection in inputConnection.Refs)
                        {
                            CollapseNodes(outConnection.Parent);
                        }
                    }
                }
            }
        }

        void Symplify()
        {
            List<PinConnection> listToRemove = new List<PinConnection>();
            do
            {
                listToRemove.Clear();
                foreach (PinConnection a_input in _mod.Connections.Where((PinConnection x) => x.InputOrBidirectional))
                {
                    if (a_input.Refs.Count != 0)
                    {
                        Node a = a_input.Parent;
                        PinConnection b_output = a_input.Refs[0];
                        Node b = b_output.Parent;
                        if (b.Type == NodeType.PinNode)
                        {
                            if (b_output.Refs.Count == 1)
                            {
                                PinConnection b_input = b.Connections.Find(x => x.DirectionType == DirectionType.Input);
                                PinConnection c_output = b_input.Refs[0];
                                a_input.Refs.Clear();
                                a_input.Refs.Add(c_output);
                                c_output.Refs.Clear();
                                c_output.Refs.Add(a_input);
                                b_input.Refs.Clear();
                                b_output.Refs.Clear();
                                listToRemove.Add(b_input);
                            }
                        }
                        else if (a.Type == NodeType.PinNode && b.Type == NodeType.Pin)
                        {
                            b_output.Refs.Remove(a_input);
                            PinConnection a_output = a.Connections.Find((PinConnection x) => x.DirectionType == DirectionType.Output);
                            foreach (PinConnection c_input in a_output.Refs)
                            {
                                c_input.Refs.Remove(a_output);
                                c_input.Refs.Add(b_output);
                                b_output.Refs.Add(c_input);
                            }
                            a_output.Refs.Clear();
                            a_input.Refs.Clear();
                            listToRemove.Add(a_input);
                        }
                    }
                }
                foreach (PinConnection removeMe in listToRemove)
                {
                    _mod.Connections.Remove(removeMe);
                }
            }
            while (listToRemove.Count > 0);
        }

        void GenerateExpressions()
        {
            _visited.Clear();
            foreach (PinConnection con in _mod.Connections)
            {
                if (con.InputOrBidirectional)
                {
                    PinConnection refToInput = con.Refs.FirstOrDefault(r => r.DirectionType == DirectionType.Output);
                    if (refToInput != null)
                    {
                        string name = con.Name;
                        if (con.Parent.Type == NodeType.PinNode || con.Parent.Type == NodeType.Pin)
                        {
                            name = ((!(con.Name == "_PIN_OUT") && !(con.Name == "_PIN_IN") && !(con.Name == con.Parent.Name)) ? (con.Parent.Name + "." + con.Name) : con.Parent.Name);
                        }
                        else if (con.Parent.Type != NodeType.Module)
                        {
                            name = con.Parent.Name + "." + con.Name;
                        }
                        _visited.Add(con.Parent);
                        Console.Write(name + " = ");
                        GenerateComboLogic(refToInput);
                        Console.WriteLine(";");
                    }
                }
            }
        }

        void GenerateComboLogic(PinConnection outputConnection)
        {
            bool skip = BeginWalkOutputConnection(outputConnection);
            Node parentNode = outputConnection.Parent;
            switch (parentNode.Type)
            {
                case NodeType.DFF:
                case NodeType.TBUF:
                    Console.Write(parentNode.Name + "." + outputConnection.Name);
                    break;
                case NodeType.Pin:
                case NodeType.PinNode:
                    Console.Write(parentNode.Name);
                    break;
                case NodeType.Module:
                    Console.Write(outputConnection.Name);
                    break;
                case NodeType.Constant:
                    Console.Write("'b'" + parentNode.Constant);
                    break;
            }
            if (!skip)
            {
                if (parentNode.Type == NodeType.Not)
                {
                    Console.Write("! ( ");
                }
                else
                {
                    Console.Write(" ( ");
                }
                bool didWriteOperator = false;
                foreach (PinConnection con in parentNode.Connections)
                {
                    if (con.InputOrBidirectional)
                    {
                        List<PinConnection> refs = con.Refs;
                        if (refs.Count != 0)
                        {
                            if (refs.Count != 1)
                            {
                                throw new ApplicationException("Invalid input reference count at " + con.Name);
                            }
                            GenerateComboLogic(con.Refs[0]);
                            if (!didWriteOperator)
                            {
                                switch (parentNode.Type)
                                {
                                    case NodeType.And:
                                        Console.Write(" & ");
                                        break;
                                    case NodeType.Or:
                                        Console.Write(" # ");
                                        break;
                                    case NodeType.Xor:
                                        Console.Write(" $ ");
                                        break;
                                    default:
                                        throw new ApplicationException($"Unknown combinational operator type '{parentNode.Type}'");
                                    case NodeType.Not:
                                        break;
                                }
                                didWriteOperator = true;
                            }
                        }
                    }
                }
                Console.Write(" )");
            }
        }

        bool BeginWalkOutputConnection(PinConnection outputNode)
        {
            bool skip = false;
            if (outputNode.DirectionType != DirectionType.Output && outputNode.DirectionType != DirectionType.Bidirectional)
            {
                throw new ApplicationException("Invalid connection processing point, node not an output " + outputNode.Name + " ");
            }
            Node parentNode = outputNode.Parent;
            NodeType type = parentNode.Type;
            NodeType nodeType = type;
            if (nodeType == NodeType.Module || (uint)(nodeType - 6) <= 4u)
            {
                skip = true;
            }
            if (_visited.Contains(parentNode))
            {
                skip = true;
            }
            _visited.Add(parentNode);
            return skip;
        }

        void PrepareSyncronousNodes()
        {
            foreach (Node node in _mod.Cells)
            {
                if (node.Type == NodeType.DFF || node.Type == NodeType.TBUF)
                {
                    foreach (PinConnection connection in node.Connections)
                    {
                        if (connection.DirectionType == DirectionType.Input)
                        {
                            _mod.Connections.Add(connection);
                        }
                    }
                }
            }
        }

        void CreatePinNodes(Node node)
        {
            if (node.Type != NodeType.PinNode && !_visited.Contains(node))
            {
                _visited.Add(node);
                foreach (PinConnection connection in node.Connections)
                {
                    if (connection.DirectionType == DirectionType.Input || connection.DirectionType == DirectionType.Bidirectional)
                    {
                        List<PinConnection> refs = connection.Refs;
                        if (refs.Count != 0)
                        {
                            if (refs.Count != 1)
                            {
                                throw new ApplicationException("Invalid number of connections to input node " + connection.Name);
                            }
                            PinConnection output = connection.Refs[0];
                            if (output.DirectionType != DirectionType.Output && output.DirectionType != DirectionType.Bidirectional)
                            {
                                throw new ApplicationException("Unknown connection to input node " + connection.Name);
                            }
                            Node parentOutputNode = output.Parent;
                            if (parentOutputNode.Type != NodeType.PinNode && (output.Refs.Count > 1 || !parentOutputNode.IsCombinational))
                            {
                                string newName = Util.GenerateName();
                                Node pinNode = new Node(newName, NodeType.PinNode);
                                PinConnection outputForPinNode = new PinConnection(pinNode, "_PIN_OUT", DirectionType.Output);
                                PinConnection inputForPinNode = new PinConnection(pinNode, "_PIN_IN", DirectionType.Input);
                                pinNode.Connections.Add(outputForPinNode);
                                pinNode.Connections.Add(inputForPinNode);
                                foreach (PinConnection inputNodeThatRefsOutput in output.Refs)
                                {
                                    if (inputNodeThatRefsOutput.Refs.Count > 1 || (inputNodeThatRefsOutput.DirectionType != DirectionType.Input && inputNodeThatRefsOutput.DirectionType != DirectionType.Bidirectional))
                                    {
                                        throw new ApplicationException("Output node references non input node");
                                    }
                                    inputNodeThatRefsOutput.Refs.Clear();
                                    inputNodeThatRefsOutput.Refs.Add(outputForPinNode);
                                    outputForPinNode.Refs.Add(inputNodeThatRefsOutput);
                                }
                                output.Refs.Clear();
                                output.Refs.Add(inputForPinNode);
                                inputForPinNode.Refs.Add(output);
                                _createdPinNodes.Add(pinNode);
                            }
                            if (parentOutputNode.IsCombinational)
                            {
                                CreatePinNodes(parentOutputNode);
                            }
                        }
                    }
                }
            }
        }
    }
}