using System;
using System.Collections.Generic;
using System.Threading;

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
        public int Id { get; }

        static int _idCounter = 1;
        
		//Use a 0 seed so we get predictable results

        public PinConnection(Node parent, string name, DirectionType directionType) 
        {
            this.Name = name;
            this.Parent = parent;
            this.DirectionType = directionType; 
        }

        public PinConnection()
        {
            Id = Interlocked.Increment(ref _idCounter);
        }
        
        public bool InputOrBidirectional => DirectionType == DirectionType.Input || DirectionType == DirectionType.Bidirectional;

        public bool OutputOrBidirectional => DirectionType == DirectionType.Output || DirectionType == DirectionType.Bidirectional;
    }
}
