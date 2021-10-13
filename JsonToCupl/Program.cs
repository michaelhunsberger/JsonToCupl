using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JsonToCupl
{
    class Program
    {
        static void Main(string[] args)
        {
            var fileName = args[0];
            using (StreamReader reader = File.OpenText(fileName))
            {
                JObject root = (JObject)JToken.ReadFrom(new JsonTextReader(reader));
                foreach (var cld in root)
                {
                    switch (cld.Key)
                    {
                        case "modules":
                            var modules = new JsonModules();
                            modules.Build(cld.Value);
                            break;

                    }
                }
            }
        }
    }

    public class JTCParseExeption : Exception
    {
        readonly JToken _tok;
        public JTCParseExeption(string message, JToken tok) : base(string.Concat(message, $" Path={tok.Path}"))
        {
        }
    }

    static class JsonObjUtil
    {
        public static T CastJson<T>(this JToken tok) where T : JToken
        {
            if (tok == null)
                throw new ArgumentNullException(nameof(tok));
            T jo = tok as T;
            if (jo == null)
                throw new JTCParseExeption($"Unexpected json token", tok);
            return jo;
        }

        public static T GetOrCreate<K, T>(this Dictionary<K, T> dic, K key) where T : new()
        {
            T ret;
            if (!dic.TryGetValue(key, out ret))
            {
                ret = new T();
            }
            return ret;
        }
    }

    enum NodeType
    {
        Pin,
        Module
    }

    enum DirectionType
    {
        Unknown,
        Input,
        Output,
        Inout
    }

    class Connection
    {
        public Node Node { get; set; }
        public string Name { get; set; }

        public Connection(string name)
        {
            Name = name;
        }
    }

    class PinConnection
    {

        readonly Node _parent;
        readonly string _name;
        readonly DirectionType _directionType;
        public readonly List<PinConnection> Refs = new List<PinConnection>();

        public DirectionType DirectionType => _directionType;
        public string Name => _name;
        internal Node Parent => _parent;

        public PinConnection(Node parent, string name, DirectionType directionType)
        {
            _name = name;
            _parent = parent;
            _directionType = directionType;
        }
    }

    class JsonPinConnection : PinConnection
    {
        readonly int _bit;
        public JsonPinConnection(Node parent, string name, DirectionType directionType, int bit) : base(parent, name, directionType)
        {
            _bit = bit;
        }

        public int Bit => _bit;
    }

    class Node
    {
        readonly string _name;
        readonly NodeType _type;
        readonly List<PinConnection> _connections = new List<PinConnection>();
        readonly List<PinConnection> _in = new List<PinConnection>();
        public Node(string name, NodeType type)
        {
            _name = name;
            _type = type;
        }

        public string Name => _name;
        public List<PinConnection> Connections => _connections;
        public NodeType Type => _type;
    }

    static class Util
    {
        public static string GenerateName(string baseName, int ix)
        {
            return string.Concat(baseName, ix.ToString());
        }
    }

    class JsonNode : Node
    {
        public JsonNode(string name, NodeType type) : base(name, type)
        {
        }
    }


    interface IJsonObj
    {
        void Build(JToken obj);
    }

    /*
     * For each input bit, find output bit pin of node
     */

    class JsonModule : JsonNode, IJsonObj
    {
        readonly Dictionary<int, JsonPinConnection> _lookup = new Dictionary<int, JsonPinConnection>();
        readonly List<JsonNode> _jsonNodes = new List<JsonNode>();
        static readonly int[] emptyBits = new int[0];

        public JsonModule(string name) : base(name, NodeType.Module)
        {

        }

        public void BuildNodeConnections()
        {
            //Build Lookup table
            foreach (var node in _jsonNodes)
            {
                foreach (var connection in node.Connections)
                {
                    if (connection.DirectionType == DirectionType.Output || connection.Parent == this)
                    {
                        JsonPinConnection con = (JsonPinConnection)connection;
                        _lookup.Add(con.Bit, con);
                    }
                }
            }

            //Build nodel input references for input and output
            foreach (var node in _jsonNodes)
            {
                foreach (var connection in node.Connections)
                {
                    if (connection.DirectionType == DirectionType.Input && connection.Parent != this)
                    {
                        JsonPinConnection con = (JsonPinConnection)connection;
                        var r = _lookup[con.Bit];
                        connection.Refs.Add(r);
                        r.Refs.Add(r);
                    }
                }
            }
        }

        public void Build(JToken tok)
        {
            JObject jo = tok.CastJson<JObject>();
            foreach (var node in jo)
            {
                switch (node.Key)
                {
                    case "ports":
                        BuildPorts(node.Value);
                        break;
                    case "cells":
                        BuildCells(node.Value);
                        break;
                }
            }
        }

        sealed class PortProps
        {
            public readonly DirectionType Direction;
            public readonly int[] Bits;
            public readonly string Name;

            public PortProps(DirectionType direction, int[] bits, string name)
            {
                this.Direction = direction;
                this.Bits = bits;
                this.Name = name;
            }
        }

        void BuildPorts(JToken value)
        {
            foreach (var oport in value.CastJson<JObject>())
            {
                string name = oport.Key;
                DirectionType direction = DirectionType.Unknown;
                int[] bits = null;
                foreach (var prop in oport.Value.CastJson<JObject>())
                {
                    switch (prop.Key)
                    {
                        case "direction":
                            var sval = (string)prop.Value;
                            direction = (DirectionType)Enum.Parse(typeof(DirectionType), sval, true);
                            break;
                        case "bits":
                            bits = prop.Value.CastJson<JArray>().Select(x => (int)x).ToArray();
                            break;
                    }
                }
                //This is an array
                bool useArrayName = bits.Length > 1;
                for (int ix = 0; ix < bits.Length; ix++)
                {
                    name = useArrayName ? Util.GenerateName(name, ix) : name;
                    JsonPinConnection pc = new JsonPinConnection(this, name, direction, bits[ix]);
                    this.Connections.Add(pc);
                }
            }
        }

        void BuildCells(JToken value)
        {
            JObject jo = value.CastJson<JObject>();
            foreach (var ocell in jo)
            {
                string name = ocell.Key;
                var props = GetCellProps(ocell.Value.CastJson<JObject>());

            }
        }

        enum ConnectionType
        {
            Unknown,
            Vert,
            Constant
        }

        sealed class CellPortProp
        {
            public DirectionType DirectionType { get; set; }
            public int BitOrConst { get; set; }
            public ConnectionType ConnectionType { get; set; }
            public string Name { get; set; }
        }

        sealed class CellProp
        {
            readonly public string Type;
            readonly public CellPortProp[] Ports;

            public CellProp(string type, CellPortProp[] ports)
            {
                this.Type = type;
                this.Ports = ports;
            }
        }

        CellProp GetCellProps(JObject jo)
        {
            string type = null;
            var map = new Dictionary<string, CellPortProp>();
            foreach (var prop in jo)
            {
                switch (prop.Key)
                {
                    case "type":
                        object o = prop.Value.CastJson<JValue>().Value;
                        type = o as string;
                        if(type == null)
                            throw new JTCParseExeption($"Unknown type literal value '{ o ?? ""}'", jo);
                        break;
                    case "port_directions":
                        var directions = GetCellPropKeyValue(prop.Value.CastJson<JObject>());
                        foreach (var dir in directions)
                        {
                            bool succeed = false;
                            DirectionType directionType = DirectionType.Unknown;
                            string sval = dir.Value as string;
                            if (sval != null)
                            {
                                if (Enum.TryParse(sval, true, out directionType))
                                    succeed = true;
                            }
                            if (!succeed)
                            {
                                throw new JTCParseExeption($"Unknown direction literal value '{dir.Value ?? ""}'", jo);
                            }
                            map.GetOrCreate(dir.Key).DirectionType = directionType;
                        }
                        break;
                    case "connections":
                        var connections = GetCellPropKeyValue(prop.Value.CastJson<JObject>());
                        foreach (var con in connections)
                        {
                            int value = 0;
                            ConnectionType ct = ConnectionType.Unknown;
                            if (con.Value is int)
                            {
                                ct = ConnectionType.Vert;
                                value = (int)con.Value;
                            }
                            else if (con.Value is string)
                            {
                                if (int.TryParse((string)con.Value, out value))
                                    ct = ConnectionType.Constant;
                            }
                            if (ct == ConnectionType.Unknown)
                                throw new JTCParseExeption($"Unknown connection literal value '{con.Value ?? ""}", jo);
                            var cellprop = map.GetOrCreate(con.Key);
                            cellprop.BitOrConst = value;
                            cellprop.ConnectionType = ct;
                        }
                        break;
                }
            }
            return new CellProp(type, map.Values.ToArray());
        }

        static IEnumerable<KeyValuePair<string, object>> GetCellPropKeyValue(JToken value)
        {
            JObject jo = value.CastJson<JObject>();
            foreach (var prop in jo)
            {
                yield return new KeyValuePair<string, object>(prop.Key, prop.Value);
            }
        }
    }


    class JsonModules : IJsonObj, IEnumerable<JsonModule>
    {
        readonly List<JsonModule> _modules = new List<JsonModule>();
        public string Name { get; private set; }

        public void Build(JToken tok)
        {
            JObject jo = tok.CastJson<JObject>();
            foreach (var cld in jo)
            {
                var module = new JsonModule(cld.Key);
                module.Build(cld.Value);
                _modules.Add(module);
            }
        }

        public IEnumerator<JsonModule> GetEnumerator()
        {
            return _modules.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _modules.GetEnumerator();
        }
    }
}
