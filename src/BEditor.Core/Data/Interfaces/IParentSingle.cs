using System.Collections.Generic;

namespace BEditor.Data
{
    /// <summary>
    /// Represents that this object has a child element of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Type of the child element.</typeparam>
    public interface IParentSingle<out T> : IParent<T>
    {
        // こう書けない
        // IEnumerable<T> IParent<T>.Children => yeied return Child;

        IEnumerable<T> IParent<T>.Children
        {
            get
            {
                yield return Child;
            }
        }

        /// <summary>
        /// Gets the child element.
        /// </summary>
        public T Child { get; }
    }
}