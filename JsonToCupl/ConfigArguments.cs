using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
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


        public string InFile { get; private set; }
        public string OutFile { get; private set; }
        public string Device { get; private set; }
        public bool GenerateYosys { get; private set; }

        bool _populateInter = false;
        public string IntermediateOutFile1 { get; private set; }
        public string IntermediateOutFile2 { get; private set; }

        string _pinFile;
        public IPins PinNums { get; private set; } = Pins.Empty;

        public static void PrintHelp(TextWriter tr)
        {
            tr.WriteLine("Yosys file generator: JsonToCupl [yosys_file_options] <infile> <outfile>");
            tr.WriteLine("yosys_file_options:");
            tr.WriteLine($"  -{ARG_GEN_YOSYS}");
            tr.WriteLine($"      Generate a yosys file.  This file can then we executed by yosys to generate a compatible json file.");
            tr.WriteLine("   <infile>");
            tr.WriteLine("          Verilog file.");
            tr.WriteLine("   <outfile>");
            tr.WriteLine("          Json output file");
            tr.WriteLine("CUPL Generator options: JsonToCupl [cupl_gen_options] <infile> <outfile>");
            tr.WriteLine("   <infile>");
            tr.WriteLine("          Json file from yosys");
            tr.WriteLine("   <outfile>");
            tr.WriteLine("          Output CUPL file");
            tr.WriteLine("cupl_gen_options:");
            tr.WriteLine($"  -{ARG_PIN_FILE} <filename>");
            tr.WriteLine("       Specifies a pin file.  If omitted, no pin numbers will be assigned to generated CUPL PIN declarations.");
            tr.WriteLine($"  -{ARG_DEVICE} <device_name>");
            tr.WriteLine("       Specifies a device name.  If omitted, device name defaults to 'virtual'");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="args"></param>
        /// <returns>Returns null if no arguments specified</returns>
        public static ConfigArguments BuildFromArgs(string[] args)
        {
            ConfigArguments ret = new ConfigArguments();
            if (args.Length == 0)
                return null; //No arguments

            if (args.Length == 1 && string.Equals(args[0].TrimStart('-'), ARG_HELP, StringComparison.OrdinalIgnoreCase))
                return null;

            int ix = 0;
            ret._pinFile = null;

            try
            {
                while (ix < args.Length - 2)
                {
                    string key = args[ix++].ToLowerInvariant();
                    if (!key.StartsWith("-"))
                        CfgThrowHelper.InvalidArgName(key);
                    key = key.Trim('-');
                    switch (key)
                    {
                        case ARG_PIN_FILE_SHORT:
                        case ARG_PIN_FILE:
                            ret._pinFile = args[ix++];
                            break;
                        case ARG_DEVICE_SHORT:
                        case ARG_DEVICE:
                            ret.Device = args[ix++];
                            break;
                        case ARG_INTER_SHORT:
                        case ARG_INTER:
                            ret._populateInter = true;
                            break;
                        case ARG_GEN_YOSYS:
                        case ARG_GEN_YOSYS_SHORT:
                            ret.GenerateYosys = true;
                            break;
                        default:
                            CfgThrowHelper.InvalidArgName(key);
                            break;
                    }
                }

                ret.InFile = args[ix++];
                ret.OutFile = args[ix];

                if (ret._populateInter)
                {
                    ret.IntermediateOutFile1 = Path.Combine(Path.GetDirectoryName(ret.OutFile),
                        Path.GetFileNameWithoutExtension(ret.OutFile) + ".it1");
                    ret.IntermediateOutFile2 = Path.Combine(Path.GetDirectoryName(ret.OutFile),
                        Path.GetFileNameWithoutExtension(ret.OutFile) + ".it2");
                }
            }
            catch (IndexOutOfRangeException)
            {
                CfgThrowHelper.InvalidNumberOfArguments();
            }
            Check(ret);
            if (!string.IsNullOrEmpty(ret._pinFile))
            {
                using (Stream fs = File.OpenRead(ret._pinFile))
                {
                    using (TextReader tr = new StreamReader(fs))
                    {
                        ret.PinNums = Pins.Build(tr);
                    }
                }
            }

            return ret;
        }

        static void Check(ConfigArguments configArguments)
        {
            if (string.IsNullOrWhiteSpace(configArguments.InFile))
                CfgThrowHelper.MissingInputFile();
            if (string.IsNullOrWhiteSpace(configArguments.OutFile))
                CfgThrowHelper.MissingOutputFile();
            if (!File.Exists(configArguments.InFile))
                CfgThrowHelper.InputFileNotFound(configArguments.InFile);
            if (!string.IsNullOrWhiteSpace(configArguments._pinFile))
            {
                if (!File.Exists(configArguments._pinFile))
                    CfgThrowHelper.PinFileNotFound(configArguments._pinFile);
            }
        }
    }
}
