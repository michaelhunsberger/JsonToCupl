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
            ConfigArguments config = new ConfigArguments(args);
 
            try
            {
                config.BuildFromArgs();
                if (config.Action == CodeGenAction.None)
                {
                    ConfigArguments.PrintHelp(Console.Out);
                    Environment.Exit(0);
                }
                switch (config.Action)
                {
                    case CodeGenAction.WinCupl:
                        GenerateCUPL(config);
                        break;
                    case CodeGenAction.Yosys:
                        GenerateYosys(config);
                        break;
                    default:
                        throw new NotImplementedException();
                }
            }
            catch (ConfigException ex)
            {
                Console.WriteLine(ex.Message);
                Environment.Exit((int)ex.CodeCode);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                Environment.Exit((int)ErrorCode.CodeGenerationError);
            }
        }

        static void GenerateYosys(IConfig config)
        {
            CodeGenYosys gen = new CodeGenYosys(config);
            if (File.Exists(config.OutFile))
                File.Delete(config.OutFile);
            using (Stream fs = File.OpenWrite(config.OutFile))
            {
                using (StreamWriter sr = new StreamWriter(fs))
                {
                    gen.GenerateCode(sr);
                    sr.Flush();
                }
            }
        }

        static void GenerateCUPL(IConfig config)
        { 
            JModuleCollection modules = GetModules(config);
            JModule mod = null;
            if (config.ModuleName != null)
                mod = modules.First(x => x.Name.Equals(config.ModuleName));

            int numOfModules = modules.Count();
            if (mod == null)
            {
                if (numOfModules > 1)
                {
                    CfgThrowHelper.AmbiguousOrModuleNotFound();
                }
                else
                {
                    mod = modules.First();
                }
            }

          
            CodeGenCupl gen = new CodeGenCupl(mod, config);
            gen.GenerateBranchingNodes();
            if (config.IntermediateOutFile1 != null)
            {
                WriteCuplCode(config.IntermediateOutFile1, gen);
            }
            gen.SimplifyConnections();
            if (config.IntermediateOutFile2 != null)
            {
                WriteCuplCode(config.IntermediateOutFile2, gen);
            }
            gen.GenerateCollapseNodes();
            gen.ExpandCombinationalPinNodes();
            string outFile = config.OutFile;
            if (outFile == null)
            {
                outFile = mod.Name + ".PLD";
            }
            WriteCuplCode(outFile, gen);
        }

        static void WriteCuplCode(string outFile, CodeGenCupl gen)
        {
            if (File.Exists(outFile))
                File.Delete(outFile);
            using (Stream fs = File.OpenWrite(outFile))
            {
                using (StreamWriter sr = new StreamWriter(fs))
                {
                    gen.GenerateCode(sr);
                    sr.Flush();
                }
            }
        }

        static JModuleCollection GetModules(IConfig config)
        {
            JModuleCollection modules = null;
            try
            {
                if(config.InFiles.Length > 1)
                {
                    CfgThrowHelper.InvalidNumberOfArguments("More than one json file specified.");
                }
                modules = GetModules(config.InFiles.First());
            }
            catch (JTCParseExeption e)
            {
                Console.WriteLine(e.Message);
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
