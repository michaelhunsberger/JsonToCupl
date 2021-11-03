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
        public const string ARG_PIN_FILE = "pinfile";
        public const string ARG_DEVICE = "device";
        public const string ARG_HELP = "help";

        public string InFile { get; private set; }
        public string OutFile { get; private set; }
        public string Device { get; private set; }

        string _pinFile;
        public IPins PinNums { get; private set; } = Pins.Empty;

        public static void PrintHelp(TextWriter tr)
        {
            tr.WriteLine("Usage: JsonToCupl [options] <infile> <outfile>");
            tr.WriteLine("   <infile>");
            tr.WriteLine("          Json file from yosys");
            tr.WriteLine("   <outfile>");
            tr.WriteLine("          Output CUPL file");
            tr.WriteLine("options:");
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
                        case ARG_PIN_FILE:
                            ret._pinFile = args[ix++];
                            break;
                        case ARG_DEVICE:
                            ret.Device = args[ix++];
                            break;
                        default:
                            CfgThrowHelper.InvalidArgName(key);
                            break;
                    }
                }

                ret.InFile = args[ix++];
                ret.OutFile = args[ix];
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
