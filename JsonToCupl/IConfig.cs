using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JsonToCupl
{
    enum CodeGenAction
    {
        /// <summary>
        /// Not doing anything?
        /// </summary>
        None,
        /// <summary>
        /// Generate a Yosys file (this will generate a json file when executed by Yosys)
        /// </summary>
        Yosys,
        /// <summary>
        /// Generate a WinCupl file (used by WinCupl to generate a JED file for programming PLDs\GALs\CPLDs\Whatnots)
        /// </summary>
        WinCupl
    }
    interface IConfig
    {
        /// <summary>
        /// Input files
        /// </summary>
        string[] InFiles { get; }
        /// <summary>
        /// Output files
        /// </summary>
        string OutFile { get; }
        /// <summary>
        /// Device name
        /// </summary>
        string Device { get; }
        /// <summary>
        /// WinCupl compatible pin file
        /// </summary>
        IPins PinNums { get; }
        /// <summary>
        /// Used for debugging only, intermediate results 1
        /// </summary>
        string IntermediateOutFile1 { get; }
        /// <summary>
        /// Used for debugging only, intermediate results 2
        /// </summary>
        string IntermediateOutFile2 { get; }
        /// <summary>
        /// Tells code generate what we are doing
        /// </summary>
        CodeGenAction Action { get; }
        /// <summary>
        /// If more than one module is defined, we need to know the target module
        /// </summary>
        string ModuleName { get; }
    }
}
