using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Extensions.Object.Datas
{
    [AttributeUsage(AttributeTargets.Class)]
    public class NamedAttribute : Attribute
    {
        public NamedAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }
}
