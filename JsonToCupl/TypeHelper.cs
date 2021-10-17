using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace JsonToCupl
{
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
}
