namespace JsonToCupl
{
    static class Util
    {
        public static string GenerateName()
        {
            var ret = string.Concat("JTCN", cnt++);
            return ret;
        }
        public static string GenerateName(string baseName, int ix)
        {
            return string.Concat(baseName, ix.ToString());
        }

        static int cnt = 0;
    }
}
