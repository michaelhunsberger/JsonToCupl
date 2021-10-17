namespace JsonToCupl
{
    static class Util
    {
        public static string GenerateName()
        {
            return string.Concat("JTCN", cnt++);
        }
        public static string GenerateName(string baseName, int ix)
        {
            return string.Concat(baseName, ix.ToString());
        }

        static int cnt = 0;
    }
}
