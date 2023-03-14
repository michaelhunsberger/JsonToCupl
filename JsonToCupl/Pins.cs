using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace JsonToCupl
{
    /// <summary>
    /// Used for the pins file in CUPL
    /// </summary>
    class Pins : IPins
    {
        readonly Dictionary<string, int> _pins;
        class NoPins : IPins
        {
            public int this[string pinName] => 0;
        }

        public static readonly IPins Empty = new NoPins();
        
        static readonly Regex regPinRegister = new Regex("^(?<pinname>.+)\\[(?<ix>\\d+)\\]$");

        Pins(Dictionary<string, int> pins)
        {
            _pins = pins;
        }

        public int this[string pinName]
        {
            get
            {
                int ret;
                _pins.TryGetValue(pinName, out ret);
                return ret;
            }
        }


        public static IPins Build(TextReader tr)
        {
            /*
             Example:
             dreset_n                     : 1         : input  : TTL               :         :           : N
            */

            Dictionary<string, int> pins = new Dictionary<string, int>();
            string line;
            while ((line = tr.ReadLine()) != null)
            {
                string[] parts = line.Split(':');
                if (parts.Length >= 2)
                {
                    string pinName = parts[0].Trim();
                    pinName = GeneratePinName(pinName);
                    string sPinNum = parts[1].Trim();
                    int pinNum;
                    if (!int.TryParse(sPinNum, out pinNum))
                    {
                        continue;
                    }
                    pins[pinName] = pinNum;
                }
            }

            return new Pins(pins);
        }
         
        static string GeneratePinName(string pinName)
        {
            Match mRegister = regPinRegister.Match(pinName);
            if (mRegister.Success)
            {
                return string.Concat(mRegister.Groups["pinname"].Value, mRegister.Groups["ix"].Value);
            }
            return pinName;
        }
    }
}
