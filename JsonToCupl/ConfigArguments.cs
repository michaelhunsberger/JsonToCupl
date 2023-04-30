using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace JsonToCupl
{
    class ConfigArguments : IConfig
    {
        /// <summary>
        /// WinCUPL pin file.  When generating the CUPL file, the PIN file will be used when assigning pin numbers.  Else, the pin numbers are left blank 
        /// and its up WinCUPL to assign pins.
        /// </summary>
        public const string ARG_PIN_FILE = "pinfile";
        public const string ARG_PIN_FILE_SHORT = "p";

        /// <summary>
        /// The target CUPL device (appears in the generated CUPL file)
        /// </summary>
        public const string ARG_DEVICE = "device";
        public const string ARG_DEVICE_SHORT = "d";

        /// <summary>
        /// Help.  Its helpful
        /// </summary>
        public const string ARG_HELP = "help";
        public const string ARG_HELP_SHORT = "h";

        /// <summary>
        /// ARG_INTER is only used for debugging, writes the intermediate results after certain CodeGen passes
        /// </summary>
        public const string ARG_INTER = "inter";
        public const string ARG_INTER_SHORT = "i";

        /// <summary>
        /// Generate a yosys file.  This file can then be consumed by yosys to generate the json file.
        /// </summary>
        public const string ARG_GEN_YOSYS = "yosys";
        public const string ARG_GEN_YOSYS_SHORT = "y";

        public const string ARG_GEN_MODULE = "module";
        public const string ARG_GEN_MODULE_SHORT = "m";

        public const string ARG_GEN_COMBIN_LIMIT = "combinlimit";
        public const string ARG_GEN_COMBIN_LIMIT_SHORT = "c";

        /// <summary>
        /// Used to specify the input file(s).  If more than one, argument must be comma delimiated
        /// </summary>
        public const string ARG_GEN_IN = "in";

        /// <summary>
        /// Used to specify the output file.
        /// </summary>
        public const string ARG_GEN_OUT = "out";
        public string[] InFiles { get; private set; }
        public string OutFile { get; private set; }
        public string Device { get; private set; }

        bool _populateInter = false;
        public string IntermediateOutFile1 { get; private set; }
        public string IntermediateOutFile2 { get; private set; }

        string _pinFile;
        public IPins PinNums { get; private set; } = Pins.Empty;

        public CodeGenAction Action { get; private set; } = CodeGenAction.None;

        public string ModuleName { get; private set; }
         

        int _ix;
        readonly string[] _args;

        public ConfigArguments(string[] args)
        {
            _args = args;
        }

        public static void PrintHelp(TextWriter tr)
        {
            tr.WriteLine("Yosys file generator: JTC [yosys_file_options] -in <infile> -out <outfile>");
            tr.WriteLine("yosys_file_options:");
            tr.WriteLine($"  -{ARG_GEN_YOSYS}");
            tr.WriteLine($"         Generate a yosys file.  This file can then be executed by yosys to generate a compatible json file.");
            tr.WriteLine($"  -{ARG_GEN_IN} filenames(s)");
            tr.WriteLine("          Verilog file.");
            tr.WriteLine($"  -{ARG_GEN_OUT} filename");
            tr.WriteLine("          Json output file");
            tr.WriteLine("CUPL Generator options: JsonToCupl [cupl_gen_options] -in <infile> -out <outfile>");
            tr.WriteLine($"  -{ARG_GEN_IN} filenames");
            tr.WriteLine("          Json file from yosys");
            tr.WriteLine($"  -{ARG_GEN_OUT} filename");
            tr.WriteLine("          Output CUPL file");
            tr.WriteLine("cupl_gen_options:");
            tr.WriteLine($"  -{ARG_PIN_FILE} <filename>");
            tr.WriteLine("          Specifies a pin file.  If omitted, no pin numbers will be assigned to generated CUPL PIN declarations.");
            tr.WriteLine($"  -{ARG_DEVICE} <device_name>");
            tr.WriteLine("          Specifies a device name.  If omitted, device name defaults to 'virtual'");
            tr.WriteLine($"  -{ARG_GEN_MODULE} <module_name>");
            tr.WriteLine("          The module within the json file to process.  If more than one module is defined, this option is required.");
            tr.WriteLine($"  -{ARG_GEN_COMBIN_LIMIT} <integer>");
            tr.WriteLine("          Limits number of buried combinational pinnodes.  If limit is reached, then the required number of pinnodes will be substituted with combinational expressions.  Pinnodes are chosen for expansion based on the least amount of added newly created nodes in the expression graph.");
            tr.WriteLine("");
        }



        /// <summary>
        /// 
        /// </summary>
        /// <param name="args"></param>
        /// <returns>Returns false if no arguments specified</returns>
        public void BuildFromArgs()
        {
            if (_args.Length == 0)
            {
                return;
            }
            if (_args.Length == 1 && string.Equals(_args[0].TrimStart('-'), ARG_HELP, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            //Default behavior, generate WinCupl 
            Action = CodeGenAction.WinCupl;

            while (!IsEnd)
            {
                string key = ReadTok().ToLowerInvariant();
                if (!key.StartsWith("-"))
                    CfgThrowHelper.InvalidArgName(key);
                key = key.Substring(1, key.Length - 1);
                ReadCodeGenOpt(key);
            }

            if (_populateInter && !string.IsNullOrEmpty(OutFile))
            {
                IntermediateOutFile1 = Path.Combine(Path.GetDirectoryName(OutFile),
                    Path.GetFileNameWithoutExtension(OutFile) + ".it1");
                IntermediateOutFile2 = Path.Combine(Path.GetDirectoryName(OutFile),
                    Path.GetFileNameWithoutExtension(OutFile) + ".it2");
            }

            Check();

            if (_pinFile != null)
            {
                using (var fs = File.OpenRead(_pinFile))
                {
                    using (StreamReader sr = new StreamReader(fs))
                    {
                        PinNums = Pins.Build(sr);
                    }
                }
            } 
        }

        void ReadCodeGenOpt(string key)
        {
            switch (key)
            {
                case ARG_PIN_FILE_SHORT:
                case ARG_PIN_FILE:
                    _pinFile = ReadRequired("Missing pin file name.");
                    break;
                case ARG_DEVICE_SHORT:
                case ARG_DEVICE:
                    Device = ReadRequired("Missing device name.");
                    break;
                case ARG_INTER_SHORT:
                case ARG_INTER:
                    _populateInter = true;
                    break;
                case ARG_GEN_YOSYS:
                case ARG_GEN_YOSYS_SHORT:
                    Action = CodeGenAction.Yosys;
                    break;
                case ARG_GEN_IN:
                    InFiles = ReadFiles();
                    if(InFiles.Length == 0)
                    {
                        CfgThrowHelper.MissingInputFile();
                    }
                    break;
                case ARG_GEN_MODULE:
                case ARG_GEN_MODULE_SHORT:
                    ModuleName = ReadRequired("Missing module name.");
                    break;
                case ARG_GEN_COMBIN_LIMIT:
                case ARG_GEN_COMBIN_LIMIT_SHORT:
                    string sCL = ReadRequired("Missing pinnode combinational limit value");
                    int icl;
                    if(!int.TryParse(sCL, out icl))
                    {
                        CfgThrowHelper.InvalidArgumentValue("Combinational limit not a integer");
                    }
                    LimitCombinationalPinNodes = icl;
                    break;
                case ARG_GEN_OUT:
                    OutFile = ReadRequired("Missing output file name.");
                    break;
                default:
                    CfgThrowHelper.InvalidArgName(key);
                    break;
            }
        }

        string[] ReadFiles()
        {
            List<string> ret = new List<string>();
            int ix;
            for(ix = _ix; ix < _args.Length; ix++)
            {
                string file = Peek(ix);
                if (file.StartsWith("-"))
                    break;
                ret.Add(file);
            }
            _ix =  ix;
            return ret.ToArray();
        }

        string ReadTok()
        {
            if (IsEnd)
                return null;
            return _args[_ix++];
        }

        string Peek(int ix)
        {
            if (ix < _args.Length)
                return _args[ix];
            else
                return null;
        }

        string ReadRequired(string errorMessage)
        {
            string ret = ReadTok();
            if (ret == null)
            {
                CfgThrowHelper.InvalidNumberOfArguments(errorMessage);
            }
            return ret;
        }

        bool IsEnd
        {
            get { return _ix >= _args.Length ? true : false; }
        }

        public int? LimitCombinationalPinNodes { get; private set; }

        void Check()
        {
            if (InFiles == null || InFiles.Length == 0)
            {
                CfgThrowHelper.MissingInputFile();
            }
            foreach(var inFile in InFiles)
            {
                if (string.IsNullOrWhiteSpace(inFile))
                    CfgThrowHelper.MissingInputFile();
                if (!File.Exists(inFile))
                    CfgThrowHelper.InputFileNotFound(inFile);
            }

            if (string.IsNullOrWhiteSpace(OutFile))
            {
                string firstInFile = InFiles.First();
                if(Action == CodeGenAction.Yosys)
                {
                    OutFile = Path.GetFileNameWithoutExtension(firstInFile) + ".ys";
                }
            }
            if (!string.IsNullOrWhiteSpace(_pinFile))
            {
                if (!File.Exists(_pinFile))
                    CfgThrowHelper.PinFileNotFound(_pinFile);
            }
        }
    }
}
