using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

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
                dic[key] = ret = new T();
            }
            return ret;
        }
    }

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
    }


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

    class Node
    {
        readonly string _name;
        readonly NodeType _type;
        readonly List<PinConnection> _connections = new List<PinConnection>();
        readonly List<PinConnection> _in = new List<PinConnection>();
        readonly int? _constant;
        public Node(string name, NodeType type, int constant)
        {
            _name = name;
            _type = type;
            _constant = constant;
        }

        public string Name => _name;
        public List<PinConnection> Connections => _connections;
        public NodeType Type => _type;
        public int? Constant => _constant;
    }

    interface IContanerNode
    {
        List<Node> Nodes { get; }
    }

    static class Util
    {
        public static string GenerateName()
        {
            return string.Concat("JTCN", cnt++);
        }
        public static string GenerateName(string baseName, int ix)
        {
            return string.Concat(baseName, ix.ToString());
        }

        static int cnt = 0;


    }

    class JsonNode : Node
    {
        public JsonNode(string name, NodeType type, int constant = 0) : base(name, type, constant)
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

    class JsonModule : JsonNode, IJsonObj, IContanerNode
    {
        readonly Dictionary<int, JsonPinConnection> _lookup = new Dictionary<int, JsonPinConnection>();
        static readonly int[] emptyBits = new int[0];
        static readonly List<Node> _nodes = new List<Node>();
        public JsonModule(string name) : base(name, NodeType.Module)
        {

        }

        public List<Node> Nodes => _nodes;

        public void BuildNodeRefs()
        {
            //Add this modules pins to the bit to reference lookup table
            foreach (var connection in Connections)
            {
                if (connection.DirectionType == DirectionType.Output)
                {
                    JsonPinConnection con = (JsonPinConnection)connection;
                    _lookup.Add(con.Bit, con);
                }
            }

            //Add all cell output nodes to the reference lookup table
            foreach (var node in Nodes)
            {
                foreach (var connection in node.Connections)
                {
                    if (connection.DirectionType == DirectionType.Output)
                    {
                        JsonPinConnection con = (JsonPinConnection)connection;
                        _lookup.Add(con.Bit, con);
                    }
                }
            }

            //Build references for cell input and outputs
            foreach (var node in Nodes)
            {
                foreach (var connection in node.Connections)
                {
                    if ((connection.DirectionType == DirectionType.Input || connection.DirectionType == DirectionType.Inout))
                    {
                        LinkConnection(connection);
                    }
                }
            }

            foreach (var connection in Connections)
            {
                if ((connection.DirectionType == DirectionType.Input || connection.DirectionType == DirectionType.Inout))
                {
                    LinkConnection(connection);
                }
            }
        }

        private void LinkConnection(PinConnection connection)
        {
            JsonPinConnection con = (JsonPinConnection)connection;
            var r = _lookup[con.Bit];
            connection.Refs.Add(r);
            r.Refs.Add(r);
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
                            direction = DirectionType.Unknown;
                            Enum.TryParse(sval, true, out direction);
                            //From the perspective of the inner cells, a input pin is actually an output pin and vice versa
                            switch (direction)
                            {
                                case DirectionType.Input:
                                    direction = DirectionType.Output;
                                    break;
                                case DirectionType.Output:
                                    direction = DirectionType.Input;
                                    break;
                                case DirectionType.Inout:
                                    break; //Nothing for this yet
                                default:
                                    throw new JTCParseExeption("Unsupported port direction value", oport.Value);
                            }
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
            int negBitCounter = -1;
            foreach (var ocell in value.CastJson<JObject>())
            {
                JsonNode jn = ConstructCell(ocell);

                foreach (var connection in jn.Connections)
                {
                    var jcon = (JsonPinConnection)connection;
                    //Fix missing direction on DFF
                    if (jn.Type == NodeType.DFF)
                    {
                        switch (connection.Name)
                        {
                            case "C":
                            case "CLR":
                            case "D":
                            case "PRE":
                                jcon.DirectionType = DirectionType.Input;
                                break;
                            case "Q":
                                jcon.DirectionType = DirectionType.Output;
                                break;
                            default:
                                throw new JTCParseExeption($"Unknown pin name {connection.Name}", ocell.Value);
                        }
                    }
                    //Check if this is a constant value (bit = 0).  Create a placeholder node for the constant
                    //Negative bit value used 
                    if (jcon.Bit == 0)
                    {
                        if (jcon.DirectionType != DirectionType.Input)
                            throw new JTCParseExeption($"Constant value connected to non input pin", ocell.Value);

                        JsonNode constNode = new JsonNode(Util.GenerateName(), NodeType.Constant, jcon.Constant);
                        constNode.Connections.Add(new JsonPinConnection(constNode, "OUT", DirectionType.Output, negBitCounter));
                        jcon.Bit = negBitCounter;
                        negBitCounter--;
                        Nodes.Add(constNode);
                    }
                }
                Nodes.Add(jn);
            }
        }

        static JsonNode ConstructCell(KeyValuePair<string, JToken> ocell)
        {
            JsonNode ret = null;
            string name = ocell.Key;
            NodeType type = NodeType.Unknown;
            var map = new Dictionary<string, JsonPinConnection>();
            foreach (var prop in ocell.Value.CastJson<JObject>())
            {
                switch (prop.Key)
                {
                    case "type":
                        object o = prop.Value.CastJson<JValue>().Value;
                        string sType = o as string;
                        if (sType == null || !TypeHelper.TryGetType(sType, out type))
                            throw new JTCParseExeption($"Unknown type literal value '{ o ?? ""}'", ocell.Value);
                        break;
                    case "port_directions":
                        var directions = GetCellPropKeyValue(prop.Value.CastJson<JObject>());
                        foreach (var dir in directions)
                        {
                            string sval = null;
                            bool succeed = false;
                            DirectionType directionType = DirectionType.Unknown;
                            var jval = dir.Value as JValue;
                            if (jval != null)
                                sval = jval.Value as string;
                            if (sval != null)
                            {
                                if (Enum.TryParse(sval, true, out directionType))
                                    succeed = true;
                            }
                            if (!succeed)
                            {
                                throw new JTCParseExeption($"Unknown direction literal value '{dir.Value ?? ""}'", ocell.Value);
                            }
                            map.GetOrCreate(dir.Key).DirectionType = directionType;
                        }
                        break;
                    case "connections":
                        var connections = GetCellPropKeyValue(prop.Value.CastJson<JObject>());
                        foreach (var con in connections)
                        {
                            bool isok = false;
                            bool isconstant = false;
                            var jarray = con.Value as JArray;
                            int ival = 0;
                            if (jarray != null && jarray.Count == 1)
                            {
                                var firstElement = jarray.First();
                                switch (firstElement.Type)
                                {
                                    case JTokenType.String:
                                        if (int.TryParse(firstElement.ToObject<string>(), out ival))
                                        {
                                            isconstant = true;
                                            isok = true;
                                        }
                                        break;
                                    case JTokenType.Integer:
                                        ival = firstElement.ToObject<int>();
                                        isok = true;
                                        break;
                                }
                            }
                            if (!isok)
                                throw new JTCParseExeption($"Unknown connection literal value '{con.Value ?? ""}", ocell.Value);
                            var cellprop = map.GetOrCreate(con.Key);
                            if (isconstant)
                                cellprop.Constant = ival;
                            else
                                cellprop.Bit = ival;
                        }
                        break;
                }
            }
            ret = new JsonNode(name, type);
            //hack, build the name of each connection based on the key value within the map
            //hack, set the parent node value based on the returned node
            foreach (var kv in map)
            {
                kv.Value.Parent = ret;
                kv.Value.Name = kv.Key;
            }
            ret.Connections.AddRange(map.Values.ToArray());
            return ret;
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

    static class TypeHelper
    {
        static readonly ReadOnlyDictionary<string, NodeType> _map;
        static TypeHelper()
        {
            Dictionary<string, NodeType> dic = new Dictionary<string, NodeType>();
            dic.Add("$_AND_", NodeType.And);
            dic.Add("$_OR_", NodeType.Or);
            dic.Add("$_NOT_", NodeType.Not);
            dic.Add("$_XOR_", NodeType.Xor);
            dic.Add("$_TBUF_", NodeType.TBUF);
            dic.Add("FDCP", NodeType.DFF);
            _map = new ReadOnlyDictionary<string, NodeType>(dic);
        }

        public static bool TryGetType(string stype, out NodeType type)
        {
            return _map.TryGetValue(stype, out type);
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
                module.BuildNodeRefs();
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
