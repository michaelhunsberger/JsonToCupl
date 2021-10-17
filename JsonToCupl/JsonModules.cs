using Newtonsoft.Json.Linq;
using System.Collections;
using System.Collections.Generic;

namespace JsonToCupl
{
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
