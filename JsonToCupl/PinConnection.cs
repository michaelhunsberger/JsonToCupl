using System;
using System.Collections.Generic;

namespace JsonToCupl
{
    enum DirectionType
    {
        Unknown,
        Input,
        Output,
        Bidirectional
    }

    class PinConnection
    {
        public Connections Refs { get; set; } = new Connections();
        public DirectionType DirectionType { get; set; } = DirectionType.Unknown;
        public string Name { get; set; }
        public Node Parent { get; set; }

        public PinConnection(Node parent, string name, DirectionType directionType)
        {
            this.Name = name;
            this.Parent = parent;
            this.DirectionType = directionType;
        }

        public PinConnection()
        {
        }
        
        public bool InputOrBidirectional { get { return DirectionType == DirectionType.Input || DirectionType == DirectionType.Bidirectional; } }

        public bool OutputOrBidirectional {  get { return DirectionType == DirectionType.Output || DirectionType == DirectionType.Bidirectional; } }
    }
}
