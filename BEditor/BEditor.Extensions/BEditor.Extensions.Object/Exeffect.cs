using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BEditor.Extensions.Object
{
    public class Exeffect
    {
        private readonly static Regex objindexRegex = new Regex(@"^\[[\d]+]\z");
        private readonly static Regex indexRegex = new Regex(@"^\[([\d]+)\.([\d]+)\]\z");

        public Exeffect(ReadOnlySpan<string> text)
        {
            if (text.Length is < 2) throw new FormatException();

            foreach (var line in text)
            {
                if (objindexRegex.IsMatch(line)) break;
                else if (indexRegex.IsMatch(line))
                {
                    if (Index is not null) break;
                    Index = indexRegex.Match(line).Groups[2].Value;
                }
                else if (line.Contains("_name=")) Name = line.Replace("_name=", "");
                else if (line.Contains('='))
                {
                    var keypair = line.Split('=');
                    Values.Add(keypair[0], keypair[1]);
                }
            }
        }

        public string Index { get; }
        public string Name { get; }
        public Dictionary<string, string> Values { get; } = new();
    }
}
