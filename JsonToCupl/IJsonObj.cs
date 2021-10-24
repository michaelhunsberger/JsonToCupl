using Newtonsoft.Json.Linq;

namespace JsonToCupl
{
    interface IJsonObj
    {
        void Build(JToken obj);
    }
}
