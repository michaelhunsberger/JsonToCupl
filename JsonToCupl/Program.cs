using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Linq;
using JsonToCuplLib;

namespace JsonToCupl
{
    class Program
    {
        static void Main(string[] args)
        {
            CodeGenRun run = new CodeGenRun(args);
            run.Run();
        }
    }
}
