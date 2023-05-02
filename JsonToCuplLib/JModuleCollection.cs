using Newtonsoft.Json.Linq;
using System.Collections;
using System.Collections.Generic;

namespace JsonToCuplLib
{
    public class JModuleCollection : IJsonObj, IEnumerable<JModule>
    {
        readonly List<JModule> _modules = new List<JModule>();

        public void Build(JToken tok)
        {
            JObject jo = tok.CastJson<JObject>();
            foreach (KeyValuePair<string, JToken> cld in jo)
            {
                JModule module = new JModule(cld.Key);
                module.Build(cld.Value);
                module.BuildNodeRefs();
                _modules.Add(module);
            }
        }

        public IEnumerator<JModule> GetEnumerator()
        {
            return _modules.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _modules.GetEnumerator();
        }
    }
}
