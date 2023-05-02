using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace JsonToCuplLib
{
    /*
     * For each input bit, find output bit pin of node
     */

    public class JModule : ContainerNode, IJsonObj
    {
        readonly Dictionary<int, JPinConnection> _lookup = new Dictionary<int, JPinConnection>();
        readonly Dictionary<int, Node> _regs = new Dictionary<int, Node>();
        int _negBitCounter = -1;

        public JModule(string name) : base(name, NodeType.Module)
        {

        }

        public void BuildNodeRefs()
        {
            //Add this modules pins to the bit to reference lookup table
            foreach (PinConnection connection in Connections)
            {
                JPinConnection con = (JPinConnection)connection;
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
            foreach (Node node in Cells)
            {
                foreach (PinConnection connection in node.Connections)
                {
                    if (connection.DirectionType == DirectionType.Output)
                    {
                        JPinConnection con = (JPinConnection)connection;
                        _lookup.Add(con.Bit, con);
                    }
                }
            }

            //Build references for cell input and outputs
            foreach (Node node in Cells)
            {
                foreach (PinConnection connection in node.Connections)
                { 
                    if ((connection.DirectionType == DirectionType.Input || connection.DirectionType == DirectionType.Bidirectional))
                    {
                        LinkConnection((JPinConnection)connection);
                    }
                }
            }

            foreach (PinConnection connection in Connections)
            {
                if ((connection.DirectionType == DirectionType.Input || connection.DirectionType == DirectionType.Bidirectional))
                {
                    JPinConnection con = (JPinConnection)connection;
                    LinkConnection(con);
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
                    case "netnames":
                        BuildNetNames(node.Value);
                        break;
                }
            }
        }

        void LinkConnection(JPinConnection con)
        {
            JPinConnection r = _lookup[con.Bit];
            con.Refs.Add(r);
            r.Refs.Add(con);
        }

        void BuildNetNames(JToken value)
        {
            foreach (KeyValuePair<string, JToken> netname in value.CastJson<JObject>())
            {
                string name = netname.Key;
                foreach(KeyValuePair<string, JToken> prop in netname.Value.CastJson<JObject>())
                {
                    switch(prop.Key)
                    {
                        case "bits":
                            int[] bits = prop.Value.CastJson<JArray>().Select(x => (int)x).ToArray();
                            bool useArrayName = bits.Length > 1;
                            for (int ix = 0; ix < bits.Length; ix++)
                            {
                                string nodename = useArrayName ? Util.GenerateName(name, ix) : name;
                                //Check if this node is contained in the list of known registers
                                if(_regs.TryGetValue(bits[ix], out Node foundReg))
                                {
                                    foundReg.Name = nodename;
                                }
                            }
                            break;
                    }
                }
            }
        }

        void BuildPorts(JToken value)
        {
            foreach (var oport in value.CastJson<JObject>())
            {
                string name = oport.Key;
                DirectionType direction = DirectionType.Unknown;
                int[] bits = null;
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
                            JArray jarray = prop.Value.CastJson<JArray>();
                            if (jarray.Count == 1 && jarray[0].Type == JTokenType.String)
                            {
                                bits = new int[] { 0 };
                                constantValue = (int)jarray[0];
                            }
                            else
                            {
                                bits = jarray.Select(x => (int)x).ToArray();
                            }
                            break;
                    }
                }

                if (bits == null)
                    throw new ApplicationException("Bits not set");
                //This is an array
                bool useArrayName = bits.Length > 1;
                for (int ix = 0; ix < bits.Length; ix++)
                {
                    var nodeName = useArrayName ? Util.GenerateName(name, ix) : name;
                    JPinConnection pc = new JPinConnection(this, nodeName, direction, bits[ix])
                    {
                        Constant = constantValue
                    };
                    this.Connections.Add(pc);
                }
            }
        }

        void BuildCells(JToken value)
        { 
            foreach (KeyValuePair<string, JToken> ocell in value.CastJson<JObject>())
            {
                Node node = ConstructCell(ocell);
                foreach (PinConnection connection in node.Connections)
                {
                    JPinConnection jcon = (JPinConnection)connection;
                    if(node.Type == NodeType.Latch)
                    {
                        switch(jcon.Name)
                        {
                            case "D":
                                jcon.Name = "L"; //L specifies its a latch
                                break;
                            case "G":
                                jcon.Name = "LE"; //LE is latch enable
                                break;
                        } 
                    }
                    //Fix missing direction on DFF
                    if (node.Type.IsDFFOrLatch())
                    {
                        switch (jcon.Name)
                        {
                            case "CK":
                            case "AR":
                            case "D":
                            case "AP":
                            case "LE":
                            case "PRE":
                            case "CLR":
                            case "L":
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
                    //Change name of tribuf to match cupl name
                    else if (node.Type == NodeType.TBUF)
                    {
                        switch (jcon.Name)
                        {
                            case "E":
                                jcon.Name = "OE";
                                break;
                        }
                    }
                    //Check if this is a constant value (bit = 0).  Create a placeholder node for the constant
                    //Negative bit value used 
                    if (jcon.Bit == 0)
                    {
                        if (jcon.DirectionType != DirectionType.Input)
                            throw new JTCParseExeption($"Constant value connected to non input connection", ocell.Value);
                        GenerateConstant(jcon);
                    }
                }
                Cells.Add(node);
            }
        }

        void GenerateConstant(JPinConnection jcon)
        {
            Node constNode = new Node(Util.GenerateName(), NodeType.Constant, jcon.Constant);
            constNode.Connections.Add(new JPinConnection(constNode, "OUT", DirectionType.Output, _negBitCounter));
            jcon.Bit = _negBitCounter;
            _negBitCounter--;
            Cells.Add(constNode);
        }

        static Node ConstructCell(KeyValuePair<string, JToken> ocell)
        {
            string name = ocell.Key;
            NodeType type = NodeType.Unknown;
            var map = new Dictionary<string, JPinConnection>();
            foreach (KeyValuePair<string, JToken> prop in ocell.Value.CastJson<JObject>())
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
                        IEnumerable<KeyValuePair<string, object>> directions = GetCellPropKeyValue(prop.Value.CastJson<JObject>());
                        foreach (var dir in directions)
                        {
                            string sval = null;
                            JValue jval = dir.Value as JValue;
                            if (jval != null)
                                sval = jval.Value as string;
                            DirectionType directionType = GetDirectionType(sval);
                            map.GetOrCreate(dir.Key).DirectionType = directionType;
                        }
                        break;
                    case "connections":
                        IEnumerable<KeyValuePair<string, object>> connections = GetCellPropKeyValue(prop.Value.CastJson<JObject>());
                        foreach (var con in connections)
                        {
                            bool isok = false;
                            bool isconstant = false;
                            JArray jarray = con.Value as JArray;
                            int ival = 0;
                            if (jarray != null && jarray.Count == 1)
                            {
                                JToken firstElement = jarray.First();
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
                            JPinConnection cellprop = map.GetOrCreate(con.Key);
                            if (isconstant)
                                cellprop.Constant = ival;
                            else
                                cellprop.Bit = ival;
                        }
                        break;
                }
            }
            Node ret = new Node(name, type);
            //hack, build the name of each connection based on the key value within the map
            //hack, set the parent node value based on the returned node
            foreach (KeyValuePair<string, JPinConnection> kv in map)
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
