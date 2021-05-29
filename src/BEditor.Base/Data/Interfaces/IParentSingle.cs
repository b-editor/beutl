// IParentSingle.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;

namespace BEditor.Data
{
    /// <summary>
    /// Represents that this object has a child element of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Type of the child element.</typeparam>
    public interface IParentSingle<out T> : IParent<T>
    {
        /// <summary>
        /// Gets the child element.
        /// </summary>
        public T Child { get; }

        /// <inheritdoc/>
        IEnumerable<T> IParent<T>.Children
        {
            get
            {
                yield return Child;
            }
        }
    }
}