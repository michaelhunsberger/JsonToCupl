namespace JsonToCupl
{
    class JsonPinConnection : PinConnection
    {
        public JsonPinConnection(Node parent, string name, DirectionType directionType, int bit) : base(parent, name, directionType)
        {
            this.Bit = bit;
        }

        public JsonPinConnection()
        {

        }
        public int Bit { get; set; }
        public int Constant { get; set; }
    }
}
