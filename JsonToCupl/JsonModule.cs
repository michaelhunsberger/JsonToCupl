using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace JsonToCupl
{
    /*
     * For each input bit, find output bit pin of node
     */

    class JsonModule : ContainerNode, IJsonObj
    {
        readonly Dictionary<int, JsonPinConnection> _lookup = new Dictionary<int, JsonPinConnection>();
        static readonly int[] emptyBits = new int[0];
        public JsonModule(string name) : base(name, NodeType.Module)
        {

        }

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
            foreach (var node in Cells)
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
            foreach (var node in Cells)
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
            r.Refs.Add(connection);
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
                Node node = ConstructCell(ocell);

                foreach (var connection in node.Connections)
                {
                    var jcon = (JsonPinConnection)connection;
                    //Fix missing direction on DFF
                    if (node.Type == NodeType.DFF)
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

                        var constNode = new Node(Util.GenerateName(), NodeType.Constant, jcon.Constant);
                        constNode.Connections.Add(new JsonPinConnection(constNode, "OUT", DirectionType.Output, negBitCounter));
                        jcon.Bit = negBitCounter;
                        negBitCounter--;
                        Cells.Add(constNode);
                    }
                }
                Cells.Add(node);
            }
        }

        static Node ConstructCell(KeyValuePair<string, JToken> ocell)
        {
            Node ret = null;
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
            ret = new Node(name, type);
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
}
