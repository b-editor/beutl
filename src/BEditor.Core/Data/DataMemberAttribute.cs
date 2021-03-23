using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Data
{
    [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
#pragma warning disable CS1591
    public sealed class DataMemberAttribute : Attribute
    {
        public DataMemberAttribute()
        {
            
        }

        public string? Name { get; set; }
    }
#pragma warning restore CS1591
}
