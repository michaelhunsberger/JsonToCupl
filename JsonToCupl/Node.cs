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

        public string Name { get; set; }
        public Connections Connections => _connections;
        public NodeType Type => _type;
        public int Constant => _constant;

        public bool IsCombinational
        {
            get { return _type != NodeType.DFF && _type != NodeType.TBUF; }
        }
         
    }
}
