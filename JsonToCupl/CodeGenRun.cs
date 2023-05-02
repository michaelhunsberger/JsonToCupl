using JsonToCuplLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace JsonToCupl
{
    sealed class CodeGenRun
    {
        readonly ConfigArguments _configs;
        public CodeGenRun(string[] args)
        {
            _configs = new ConfigArguments(args);
        }

        public void Run()
        {
            try
            {
                _configs.BuildFromArgs();
                if (_configs.Action == CodeGenAction.None)
                {
                    ConfigArguments.PrintHelp(Console.Out);
                    Environment.Exit(0);
                }
                switch (_configs.Action)
                {
                    case CodeGenAction.WinCupl:
                        GenerateCUPL();
                        break;
                    case CodeGenAction.Yosys:
                        GenerateYosys();
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

        void GenerateYosys()
        {
            CodeGenYosys gen = new CodeGenYosys(_configs);
            if (File.Exists(_configs.OutFile))
                File.Delete(_configs.OutFile);
            using (Stream fs = File.OpenWrite(_configs.OutFile))
            {
                using (StreamWriter sr = new StreamWriter(fs))
                {
                    gen.GenerateCode(sr);
                    sr.Flush();
                }
            }
        }

        void GenerateCUPL()
        {
            JModuleCollection modules = GetModules();
            JModule mod = null;
            if (_configs.ModuleName != null)
                mod = modules.First(x => x.Name.Equals(_configs.ModuleName));

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

            CodeGenCupl gen = new CodeGenCupl(mod, _configs);
            gen.GenerateBranchingNodes();
            if (_configs.IntermediateOutFile1 != null)
            {
                WriteCuplCode(_configs.IntermediateOutFile1, gen);
            }
            gen.SimplifyConnections();
            if (_configs.IntermediateOutFile2 != null)
            {
                WriteCuplCode(_configs.IntermediateOutFile2, gen);
            }
            gen.GenerateCollapseNodes();
            gen.ExpandCombinationalPinNodes();
            gen.FixPinNames();
            string outFile = _configs.OutFile;
            if (outFile == null)
            {
                outFile = mod.Name + ".PLD";
            }
            WriteCuplCode(outFile, gen);
        }

        void WriteCuplCode(string outFile, CodeGenCupl gen)
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

        JModuleCollection GetModules()
        {
            JModuleCollection modules = null;
            try
            {
                if (_configs.InFiles.Length > 1)
                {
                    CfgThrowHelper.InvalidNumberOfArguments("More than one json file specified.");
                }
                modules = GetModules(_configs.InFiles.First());
            }
            catch (JTCParseExeption e)
            {
                Console.WriteLine(e.Message);
                Environment.Exit((int)ErrorCode.InvalidJsonFile);
            }

            return modules;
        }

        JModuleCollection GetModules(string fileName)
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
