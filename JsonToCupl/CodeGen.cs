// JsonToCupl.CodeGen
using JsonToCupl;
using System;
using System.Collections.Generic;

internal class CodeGen
{
    class Pinnode
    {
        public Node Node;

        public string Name;
    }

    class CodeGenCtx
    {
        public List<Node> CreatedPinNodes = new List<Node>();
    }

    readonly ContainerNode _mod;

    readonly HashSet<Node> _visited = new HashSet<Node>();

    public CodeGen(ContainerNode mod)
    {
        _mod = mod;
    }

    public void Test()
    {
        CodeGenCtx ctx = new CodeGenCtx();
        PrepareSyncronousNodes(_mod, ctx);
        CreatePinNodes(_mod, ctx);
        foreach (Node pinNode in ctx.CreatedPinNodes)
        {
            foreach (PinConnection pinNodeCon in pinNode.Connections)
            {
                if (pinNodeCon.DirectionType == DirectionType.Input)
                {
                    _mod.Connections.Add(pinNodeCon);
                }
            }
        }
        CollapseNodes(_mod, ctx);
        GenerateExpressions(_mod, ctx);
        foreach (var cell in _mod.Cells)
        {
            if (!_visited.Contains(cell))
                throw new ApplicationException($"Unvisited cell {cell.Name}");
        }
    }

    void CollapseNodes(ContainerNode module, CodeGenCtx ctx)
    {
        var listToRemove = new List<PinConnection>();
        do
        {
            listToRemove.Clear();
            // A => B => C = A => C
            foreach (var a_input in module.Connections.FindAll(x => x.IsInput))
            {
                if (a_input.Refs.Count == 0)
                    continue;

                var b_output = a_input.Refs[0];
                var b = b_output.Parent;
                if (b.Type == NodeType.PinNode && b_output.Refs.Count == 1)
                {
                    var b_input = b.Connections.Find(x => x.DirectionType == DirectionType.Input);
                    var c_output = b_input.Refs[0];

                    //connect input a to output c
                    a_input.Refs.Clear();
                    a_input.Refs.Add(c_output);
                    c_output.Refs.Clear();
                    c_output.Refs.Add(a_input);
                    b_input.Refs.Clear();
                    b_output.Refs.Clear();
                    listToRemove.Add(b_input);
                }
            }
            foreach (var removeMe in listToRemove)
            {
                module.Connections.Remove(removeMe);
            }
        } while (listToRemove.Count > 0);
    }

    void GenerateExpressions(ContainerNode module, CodeGenCtx ctx)
    {
        _visited.Clear();
        foreach (var con in module.Connections)
        {
            if (con.IsInput)
            {
                //  _visited.Clear();
                var refs = con.Refs;
                if (refs.Count == 0)
                    continue; //should not happen
                if (refs.Count != 1)
                    throw new ApplicationException($"Invalid input reference count at {con.Name}");

                string name = con.Name;
                if (con.Parent.Type == NodeType.PinNode)
                    name = con.Parent.Name;
                else if (con.Parent.Type != NodeType.Module)
                    name = con.Parent.Name + "." + con.Name;
                _visited.Add(con.Parent);
                Console.Write(name + " = ");
                GenerateComboLogic(refs[0], ctx);
                Console.WriteLine(";");
            }
        }
    }

    void GenerateComboLogic(PinConnection outputNode, CodeGenCtx ctx)
    {
        bool skip = false;
        Node parentNode = outputNode.Parent;
        switch (parentNode.Type)
        {
            case NodeType.DFF:
            case NodeType.TBUF:
                Console.Write(parentNode.Name + "." + outputNode.Name);
                skip = true;
                break;
            case NodeType.PinNode:
                Console.Write(parentNode.Name);
                skip = true;
                break;
            case NodeType.Module:
                Console.Write(outputNode.Name);
                skip = true;
                break;
            case NodeType.Constant:
                Console.Write(parentNode.Constant);
                skip = true;
                break;
        }
        if (_visited.Contains(parentNode))
        {
            skip = true;
        }
        _visited.Add(parentNode);
        if (skip)
            return; //Skip processing this connection
        if (parentNode.Type == NodeType.Not)
        {
            Console.Write("! ( ");
        }
        else
        {
            Console.Write(" ( ");
        }
        bool didWriteOperator = false;
        foreach (var con in parentNode.Connections)
        {
            if (con.IsInput)
            {
                var refs = con.Refs;
                if (refs.Count == 0)
                    continue; //should not happen
                if (refs.Count != 1)
                    throw new ApplicationException($"Invalid input reference count at {con.Name}");

                GenerateComboLogic(con.Refs[0], ctx);
                if (!didWriteOperator)
                {
                    switch (parentNode.Type)
                    {
                        case NodeType.And:
                            Console.Write(" & ");
                            break;
                        case NodeType.Or:
                            Console.Write(" | ");
                            break;
                        case NodeType.Xor:
                            Console.Write(" # ");
                            break;
                        case NodeType.Not:
                            break; //Not a binary operator
                        default:
                            throw new ApplicationException($"Unknown combinational operator type '{parentNode.Type}'");
                    }
                    didWriteOperator = true;
                }
            }
        }
        Console.Write(" )");
    }

    void PrepareSyncronousNodes(ContainerNode module, CodeGenCtx ctx)
    {
        foreach (var node in module.Cells)
        {
            if (node.Type == NodeType.DFF || node.Type == NodeType.TBUF)
            {
                foreach (var connection in node.Connections)
                {
                    if (connection.DirectionType == DirectionType.Input)
                    {
                        module.Connections.Add(connection);
                    }
                }
            }
        }
    }

    void CreatePinNodes(Node node, CodeGenCtx ctx)
    {
        if (node.Type != NodeType.PinNode && !_visited.Contains(node))
        {
            _visited.Add(node);
            foreach (PinConnection connection in node.Connections)
            {
                if (connection.DirectionType == DirectionType.Input || connection.DirectionType == DirectionType.Inout)
                {
                    List<PinConnection> refs = connection.Refs;
                    if (refs.Count != 0)
                    {
                        if (refs.Count != 1)
                        {
                            throw new ApplicationException("Invalid number of connections to input node " + connection.Name);
                        }
                        PinConnection output = connection.Refs[0];
                        if (output.DirectionType != DirectionType.Output && output.DirectionType != DirectionType.Inout)
                        {
                            throw new ApplicationException("Unknown connection to input node " + connection.Name);
                        }
                        Node parentOutputNode = output.Parent;

                        //If the output connection to the input has more than one reference, or not a combinational gate, then create a pinnode
                        if (parentOutputNode.Type != NodeType.PinNode && (output.Refs.Count > 1 || !parentOutputNode.IsCombinational))
                        {
                            string newName = Util.GenerateName();
                            Node pinNode = new Node(newName, NodeType.PinNode);
                            PinConnection outputForPinNode = new PinConnection(pinNode, Util.GenerateName(), DirectionType.Output);
                            PinConnection inputForPinNode = new PinConnection(pinNode, Util.GenerateName(), DirectionType.Input);
                            pinNode.Connections.Add(outputForPinNode);
                            pinNode.Connections.Add(inputForPinNode);
                            foreach (PinConnection inputNodeThatRefsOutput in output.Refs)
                            {
                                if (inputNodeThatRefsOutput.Refs.Count > 1 || (inputNodeThatRefsOutput.DirectionType != DirectionType.Input && inputNodeThatRefsOutput.DirectionType != DirectionType.Inout))
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
                            ctx.CreatedPinNodes.Add(pinNode);
                        }
                        //only walk though this node if its a cominational node
                        if (parentOutputNode.IsCombinational)
                            CreatePinNodes(parentOutputNode, ctx);
                    }
                }
            }
        }
    }
}
