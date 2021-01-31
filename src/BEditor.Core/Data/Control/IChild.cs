using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Property;

namespace BEditor.Core.Data
{
    /// <summary>
    /// Represents that this object has a parent element of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Type of the parent element</typeparam>
    public interface IChild<out T>
    {
        /// <summary>
        /// Get the parent element
        /// </summary>
        public T? Parent { get; }
    }
}
