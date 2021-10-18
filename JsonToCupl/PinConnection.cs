using System.Collections.Generic;

namespace JsonToCupl
{
    enum DirectionType
    {
        Unknown,
        Input,
        Output,
        Inout
    }

    class PinConnection
    {
        public List<PinConnection> Refs { get; set; } = new List<PinConnection>();
        public DirectionType DirectionType { get; set; } = DirectionType.Unknown;
        public string Name { get; set; }
        internal Node Parent { get; set; }

        public PinConnection(Node parent, string name, DirectionType directionType)
        {
            this.Name = name;
            this.Parent = parent;
            this.DirectionType = directionType;
        }

        public PinConnection()
        {
        }
        
        public bool IsInput { get { return DirectionType == DirectionType.Input || DirectionType == DirectionType.Inout; } }
    }
}
