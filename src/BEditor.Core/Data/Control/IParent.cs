using System.Collections.Generic;

namespace BEditor.Data
{
    /// <summary>
    /// Represents that this object has a child elements of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Type of the child element</typeparam>
    public interface IParent<out T>
    {
        /// <summary>
        /// Get the child elements
        /// </summary>
        public IEnumerable<T> Children { get; }
    }
}
