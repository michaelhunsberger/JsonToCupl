using Newtonsoft.Json.Linq;
using System;

namespace JsonToCupl
{
    public class JTCParseExeption : Exception
    {
        public readonly JToken JToken;
        public JTCParseExeption(string message, JToken tok) : base(message)
        {
            this.JToken = tok;
        }

        public override string ToString()
        {
            return string.Concat("Path=", JToken.Path, Environment.NewLine, base.ToString());
        }
    }
}
