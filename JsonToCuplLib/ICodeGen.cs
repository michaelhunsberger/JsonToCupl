using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JsonToCuplLib
{
    public abstract class CodeGenBase
    {
        public abstract void GenerateCode(TextWriter tr);

        protected static void Assert(bool expression, string errorMessage)
        {
            if(!expression)
            {
                throw new CodeGenException(errorMessage);
            }
        }
    }
}
