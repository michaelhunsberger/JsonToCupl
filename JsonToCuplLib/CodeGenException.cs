using System;
using System.Runtime.Serialization;

namespace JsonToCuplLib
{
    class CodeGenException : Exception
    {
        public CodeGenException(string message) : base(message)
        {
        }

        public CodeGenException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}