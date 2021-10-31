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
        readonly NodeType _type;
        readonly Connections _connections = new Connections();
        readonly Connections _in = new Connections();
        readonly int _constant;

        public Node(string name, NodeType type, int constant = 0)
        {
            Name = name;
            _type = type;
            _constant = constant;
        }

        public NodeProcessState NodeProcessState;
        public string Name { get; set; }
        public Connections Connections => _connections;
        public NodeType Type => _type;
        public int Constant => _constant;
    }
}
