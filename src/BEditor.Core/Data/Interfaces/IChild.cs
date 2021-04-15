using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data.Property;

namespace BEditor.Data
{
    /// <summary>
    /// Represents that this object has a parent element of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Type of the parent element.</typeparam>
    public interface IChild<out T>
    {
        // IChild <T>
        // public T Parent { get; set; }
        // 上だとDataExtensions.GetParent2... が使えない

        /// <summary>
        /// Gets the parent element.
        /// </summary>
        public T Parent { get; }
    }
}