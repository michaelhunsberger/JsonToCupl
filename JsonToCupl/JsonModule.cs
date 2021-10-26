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
        readonly Dictionary<int, Node> _regs = new Dictionary<int, Node>();
        static readonly int[] emptyBits = new int[0];

        int _negBitCounter = -1;

        public JsonModule(string name) : base(name, NodeType.Module)
        {

        }

        public void BuildNodeRefs()
        {
            //Add this modules pins to the bit to reference lookup table
            foreach (var connection in Connections)
            {
                JsonPinConnection con = (JsonPinConnection)connection;
                if(connection.DirectionType == DirectionType.Input)
                {
                    if (con.Bit == 0)
                    {
                        GenerateConstant(con);
                    }
                }
                else if (connection.DirectionType == DirectionType.Output)
                { 
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
                    if ((connection.DirectionType == DirectionType.Input || connection.DirectionType == DirectionType.Bidirectional))
                    {
                        LinkConnection((JsonPinConnection)connection);
                    }
                }
            }

            foreach (var connection in Connections)
            {
                if ((connection.DirectionType == DirectionType.Input || connection.DirectionType == DirectionType.Bidirectional))
                {
                    JsonPinConnection con = (JsonPinConnection)connection;
                    LinkConnection(con);
                }
            }
        }

        private void LinkConnection(JsonPinConnection con)
        {
            var r = _lookup[con.Bit];
            con.Refs.Add(r);
            r.Refs.Add(con);
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
                    case "netnames":
                        BuildNetNames(node.Value);
                        break;
                }
            }
        }

        void BuildNetNames(JToken value)
        {
            foreach (var netname in value.CastJson<JObject>())
            {
                string name = netname.Key;
                foreach(var prop in netname.Value.CastJson<JObject>())
                {
                    switch(prop.Key)
                    {
                        case "bits":
                            var bits = prop.Value.CastJson<JArray>().Select(x => (int)x).ToArray();
                            bool useArrayName = bits.Length > 1;
                            for (int ix = 0; ix < bits.Length; ix++)
                            {
                                var nodename = useArrayName ? Util.GenerateName(name, ix) : name;
                                //Check if this node is contained in the list of known registers
                                Node foundReg;
                                if(_regs.TryGetValue(bits[ix], out foundReg))
                                {
                                    foundReg.Name = nodename;
                                }
                            }
                            break;
                    }
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
                bool isconstant = false;
                int constantValue = 0;
                foreach (var prop in oport.Value.CastJson<JObject>())
                {
                    switch (prop.Key)
                    {
                        case "direction":
                            var sval = (string)prop.Value;
                            direction = GetDirectionType(sval);
                            //From the perspective of the inner cells, a input pin is actually an output pin and vice versa
                            switch (direction)
                            {
                                case DirectionType.Input:
                                    direction = DirectionType.Output;
                                    break;
                                case DirectionType.Output:
                                    direction = DirectionType.Input;
                                    break;
                                case DirectionType.Bidirectional:
                                    break; //Nothing for this yet
                                default:
                                    throw new JTCParseExeption("Unsupported port direction value", oport.Value);
                            }
                            break;
                        case "bits":
                            var jarray = prop.Value.CastJson<JArray>();
                            if (jarray.Count == 1 && jarray[0].Type == JTokenType.String)
                            {
                                bits = new int[] { 0 };
                                isconstant = true;
                                constantValue = (int)jarray[0];
                            }
                            else
                            {
                                bits = jarray.Select(x => (int)x).ToArray();
                            }
                            break;
                    }
                }
                //This is an array
                bool useArrayName = bits.Length > 1;
                for (int ix = 0; ix < bits.Length; ix++)
                {
                    var nodename = useArrayName ? Util.GenerateName(name, ix) : name;
                    JsonPinConnection pc = new JsonPinConnection(this, nodename, direction, bits[ix]);
                    pc.Constant = constantValue;
                    this.Connections.Add(pc);
                }
            }
        }

        void BuildCells(JToken value)
        { 
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

                                //Add register to list of registers
                                _regs.Add(jcon.Bit, node);
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
                        GenerateConstant(jcon);
                    }
                }
                Cells.Add(node);
            }
        }

        void GenerateConstant(JsonPinConnection jcon)
        {
            var constNode = new Node(Util.GenerateName(), NodeType.Constant, jcon.Constant);
            constNode.Connections.Add(new JsonPinConnection(constNode, "OUT", DirectionType.Output, _negBitCounter));
            jcon.Bit = _negBitCounter;
            _negBitCounter--;
            Cells.Add(constNode);
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
                            var jval = dir.Value as JValue;
                            if (jval != null)
                                sval = jval.Value as string;
                            DirectionType directionType = GetDirectionType(sval);
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

        static DirectionType GetDirectionType(string s)
        {
            switch(s)
            {
                case "input": return DirectionType.Input; 
                case "output": return DirectionType.Output; 
                case "inout": return DirectionType.Bidirectional;
                default: return DirectionType.Unknown;
            }
        }
    }
}
