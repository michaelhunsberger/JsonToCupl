using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace JsonToCupl
{
    static class JUtil
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
}
