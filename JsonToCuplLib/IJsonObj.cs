using Newtonsoft.Json.Linq;

namespace JsonToCuplLib
{
    interface IJsonObj
    {
        void Build(JToken obj);
    }
}
