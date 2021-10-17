namespace JsonToCupl
{
    class Connection
    {
        public Node Node { get; set; }
        public string Name { get; set; }

        public Connection(string name)
        {
            Name = name;
        }
    }
}
