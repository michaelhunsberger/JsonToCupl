using System;
using System.Collections.Generic;

namespace JsonToCupl
{
    enum NodeType
    {
        Unknown,
        Module,
        And,
        Or,
        Xor,
        Not,
        DFF,
        TBUF,
        Constant,
        Pin,
        PinNode
    }

    [Flags]
    enum NodeProcessState
    {
        None = 0,
        MergeDFF = (1 << 0),
        MergeTBUF = (1 << 1),
    }

    class Node
    {
        public Node(string name, NodeType type, int constant = 0)
        {
            Name = name;
            Type = type;
            Constant = constant;
        }

        public NodeProcessState NodeProcessState { get; set; } = NodeProcessState.None;
        public string Name { get; set; }
        public Connections Connections { get; } = new Connections();
        public NodeType Type { get; }
        public int Constant { get; }
    }
}
