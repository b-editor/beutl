using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Data
{
    /// <summary>
    /// When applied to a member of type, it specifies that the member is subject to serialization and can be serialized by <see cref="Serialize"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public sealed class DataMemberAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DataMemberAttribute"/> class.
        /// </summary>
        public DataMemberAttribute()
        {
        }

        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        public string? Name { get; set; }
    }
}
