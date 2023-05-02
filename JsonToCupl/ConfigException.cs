using System;

namespace JsonToCupl
{
    class ConfigException : Exception
    {
        public readonly ErrorCode CodeCode;
        public ConfigException(string message, ErrorCode errorCodeCode) : base(message)
        {
            this.CodeCode = errorCodeCode;
        }
    }
}