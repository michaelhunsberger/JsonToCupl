﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JsonToCupl
{
    class CodeGenYosys : CodeGenBase
    {
        readonly IConfig _config;

        public CodeGenYosys(IConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// This is very simple.  This just generates the script that yosys will use to generate a json file.
        /// The script reads the verilog file, and reduces the design into simple and, or, not, xor, dff, and latches (no multiplexers)
        /// After the reduction, the contents are written to a json file.
        /// 
        /// This resulting json file can then be read and processed by the CodeGenCupl code, generating a cupl.  Once we have the cupl file,
        /// we can just use the provided (free) Amtel WinCupl compiler.
        /// 
        /// One could write a batch file to automate the entire thing--calling JsonToCupl to generate the .y yosys file, yosys to generate 
        /// the json, then JSonToCupl again to generate the final cupl file.
        /// </summary>
        /// <param name="tr"></param>
        public override void GenerateCode(TextWriter tr)
        {
            string currentPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string exeDir = Path.GetDirectoryName(currentPath);
            string pathToCuplLatchMap = Path.Combine(exeDir, "Yosys", "cupl_cells_latch.v");
            string pathToDFFLib = Path.Combine(exeDir, "Yosys", "cupl_dff.lib");
            tr.WriteLine($"read_verilog {_config.InFile}");
            tr.WriteLine($"hierarchy");
            tr.WriteLine($"proc");
            tr.WriteLine($"flatten");
            tr.WriteLine($"tribuf -logic");
            tr.WriteLine($"opt");
            tr.WriteLine($"techmap -map +/techmap.v -map {pathToCuplLatchMap}");
            tr.WriteLine($"opt");
            tr.WriteLine($"dfflibmap -prepare -liberty {pathToDFFLib}");
            tr.WriteLine($"abc -g AND,XOR");
            tr.WriteLine($"clean");
            tr.WriteLine($"dfflibmap -liberty {pathToDFFLib}");
            tr.WriteLine($"opt");
            tr.WriteLine($"write_json {_config.OutFile}");
        }
    }
}
