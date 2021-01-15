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
        public Exeffect(Dictionary<string, string> pairs)
        {
            Values = pairs;
            Name = pairs["_name"];
        }

        public string Name { get; }
        public Dictionary<string, string> Values { get; } = new();
    }
}
