using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JsonToCupl
{
    interface IConfig
    {
        string InFile { get; }
        string OutFile { get; }
        string Device { get; }
        IPins PinNums { get; }
        string IntermediateOutFile1 { get; }
        string IntermediateOutFile2 { get; }
        bool GenerateYosys { get; }
    }
}
