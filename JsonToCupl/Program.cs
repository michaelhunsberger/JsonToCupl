using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace JsonToCupl
{
    class Program
    {
        static void Main(string[] args)
        {
            var fileName = args[0];
            JsonModules modules = GetModules(fileName);
            foreach(var mod in modules)
            {
                CodeGen gen = new CodeGen(mod);
                gen.Generate(Console.Out);
            }
        }

        private static JsonModules GetModules(string fileName)
        {
            JsonModules modules = null;
            using (StreamReader reader = File.OpenText(fileName))
            {
                JObject root = (JObject)JToken.ReadFrom(new JsonTextReader(reader));
                foreach (var cld in root)
                {
                    switch (cld.Key)
                    {
                        case "modules":
                            modules = new JsonModules();
                            modules.Build(cld.Value);
                            break;

                    }
                }
            } 
            return modules;
        }
    }
}
