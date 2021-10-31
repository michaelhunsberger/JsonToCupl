using Newtonsoft.Json.Linq;
using System;

namespace JsonToCupl
{
    public class JTCParseExeption : Exception
    {
        public JTCParseExeption(string message, JToken tok) : base(string.Concat(message, $" Path={tok.Path}"))
        {
        }
    }
}
