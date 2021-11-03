using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Linq;

namespace JsonToCupl
{
    class Program
    { 
        static void Main(string[] args)
        {
            IConfig config = null;
            try
            {
                config = ConfigArguments.BuildFromArgs(args);
                if (config == null)
                {
                    ConfigArguments.PrintHelp(Console.Out);
                    Environment.Exit(0);
                }
            }
            catch (ConfigException ex)
            {
                Console.WriteLine(ex.Message);
                Environment.Exit((int)ex.CodeCode);
            }


            //Generate CUPL
            CodeGen gen = null;
            try
            {
                JModuleCollection modules = GetModules(config);
                JModule mod = modules.First();
                gen = new CodeGen(mod, config);
                gen.Generate();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                Environment.Exit((int)ErrorCode.CodeGenerationError);
            }

            //Write CUPL
            string outFile = config.OutFile;
            using (Stream fs = File.OpenWrite(outFile))
            {
                StreamWriter sr = new StreamWriter(fs);
                gen.WriteCUPL(sr);
            }
        }

        static JModuleCollection GetModules(IConfig config)
        {
            JModuleCollection modules = null;
            try
            {
                modules = GetModules(config.InFile);
            }
            catch (JTCParseExeption e)
            {
                Console.WriteLine(e.Message);
                Environment.Exit((int)ErrorCode.InvalidJsonFile);
            }

            int numOfModules = modules.Count();
            if (numOfModules != 1)
            {
                Console.WriteLine("Invalid number of modules");
                Environment.Exit((int)ErrorCode.InvalidJsonFile);
            }

            return modules;
        }
         
        static JModuleCollection GetModules(string fileName)
        {
            JModuleCollection modules = null;
            using (StreamReader reader = File.OpenText(fileName))
            {
                JObject root = (JObject)JToken.ReadFrom(new JsonTextReader(reader));
                foreach (KeyValuePair<string, JToken> cld in root)
                {
                    switch (cld.Key)
                    {
                        case "modules":
                            modules = new JModuleCollection();
                            modules.Build(cld.Value);
                            break;

                    }
                }
            }
            return modules;
        }
    }
}
