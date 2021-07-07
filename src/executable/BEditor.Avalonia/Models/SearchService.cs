using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BEditor.Models
{
    public static class SearchService
    {
        public static Regex[] CreateRegices(string pattern)
        {
            return pattern.ToUpperInvariant()
                .Split(' ')
                .Select(i => i.Trim())
                .Where(i => !string.IsNullOrWhiteSpace(i))
                .Select(i => Regex.Replace(i, ".", m =>
                {
                    var s = m.Value;
                    if (s.Equals("?"))
                    {
                        return ".";
                    }
                    else if (s.Equals("*"))
                    {
                        return ".*";
                    }
                    else
                    {
                        return Regex.Escape(s);
                    }
                }))
                .Select(i => new Regex(i))
                .ToArray();
        }

        public static bool IsMatch(Regex[] regices, string str)
        {
            var upper = str.ToUpperInvariant();
            var result = false;

            foreach (var item in regices)
            {
                result |= item.IsMatch(upper);
            }

            return result;
        }
    }
}