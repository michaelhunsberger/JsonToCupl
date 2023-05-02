using System;
using System.Collections.Generic;
using System.Threading;

namespace JsonToCuplLib
{
    public enum DirectionType
    {
        Unknown,
        Input,
        Output,
        Bidirectional
    }

    /// <summary>
    /// TODO, perhaps a better name would have been PortConnection, 'pin' implies external connection outside of a module
    /// </summary>
    public class PinConnection
    {
        public Connections Refs { get; set; } = new Connections();
        public DirectionType DirectionType { get; set; } = DirectionType.Unknown;
        public string Name { get; set; }
        public Node Parent { get; set; }
        public int Id { get; }

        static int _idCounter = 1;

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