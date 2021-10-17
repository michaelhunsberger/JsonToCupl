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
        PinNode
    }

    class Node
    {
        readonly string _name;
        readonly NodeType _type;
        readonly List<PinConnection> _connections = new List<PinConnection>();
        readonly List<PinConnection> _in = new List<PinConnection>();
        readonly int _constant;
        public Node(string name, NodeType type, int constant = 0)
        {
            _name = name;
            _type = type;
            _constant = constant;
        }

        public string Name => _name;
        public List<PinConnection> Connections => _connections;
        public NodeType Type => _type;
        public int Constant => _constant;
    }
}
