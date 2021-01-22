using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BEditor.Extensions.Object
{
    public class RawExeffect
    {
        public RawExeffect(int number, Dictionary<string, string> pairs)
        {
            Number = number;
            Values = pairs;
            Name = pairs["_name"];
        }

        public int Number { get; }
        public string Name { get; }
        public Dictionary<string, string> Values { get; } = new();
    }
}
