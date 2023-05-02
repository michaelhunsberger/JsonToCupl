using System;
using System.Collections.Generic;
using System.Threading;

namespace JsonToCuplLib
{
    static class Util
    {
        static int _cnt = 0;

        /// <summary>
        /// Generate a unique a name
        /// </summary>
        /// <returns></returns>
        public static string GenerateName()
        {
            int id = Interlocked.Increment(ref _cnt);
            string ret = string.Concat("JTCN", id);
            return ret;
        }

        /// <summary>
        /// Generates an indexed name identifier 
        /// </summary>
        /// <param name="baseName"></param>
        /// <param name="ix"></param>
        /// <returns></returns>
        public static string GenerateName(string baseName, int ix)
        {
            return string.Concat(baseName, ix.ToString());
        }

        /// <summary>
        /// https://www.rosettacode.org/wiki/Word_wrap
        /// </summary>
        /// <param name="text"></param>
        /// <param name="lineWidth"></param>
        /// <returns></returns>
        public static string Wrap(string text, int lineWidth)
        {
            return string.Join(string.Empty,
                Wrap(
                    text.Split(new char[0],
                        StringSplitOptions
                            .RemoveEmptyEntries),
                    lineWidth));
        }

        /// <summary>
        /// https://www.rosettacode.org/wiki/Word_wrap
        /// </summary>
        /// <param name="words"></param>
        /// <param name="lineWidth"></param>
        /// <returns></returns>
        public static IEnumerable<string> Wrap(IEnumerable<string> words,
            int lineWidth)
        {
            var currentWidth = 0;
            foreach (var word in words)
            {
                if (currentWidth != 0)
                {
                    if (currentWidth + word.Length < lineWidth)
                    {
                        currentWidth++;
                        yield return " ";
                    }
                    else
                    {
                        currentWidth = 0;
                        yield return "\r\n  ";
                        //WinCupl must always have "\r\n", despite what environment this runs in
                        //yield return "Environment.NewLine;"
                    }
                }
                currentWidth += word.Length;
                yield return word;
            }
        }
    }
}
