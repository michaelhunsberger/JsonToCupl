namespace JsonToCuplLib
{
    class JPinConnection : PinConnection
    {
        public JPinConnection(Node parent, string name, DirectionType directionType, int bit) : base(parent, name, directionType)
        {
            this.Bit = bit;
        }

        public JPinConnection()
        {

        }

        public int Bit { get; set; }
        public int Constant { get; set; }
    }
}
