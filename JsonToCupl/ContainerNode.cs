using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JsonToCupl
{
    class ContainerNode : Node
    {
        public ContainerNode(string name, NodeType type, int constant = 0) : base(name, type, constant)
        {
        }

        public List<Node> Cells { get; } = new List<Node>();
    }
}
