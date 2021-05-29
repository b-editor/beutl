// IParent.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;

namespace BEditor.Data
{
    /// <summary>
    /// Represents that this object has a child elements of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Type of the child element.</typeparam>
    public interface IParent<out T>
    {
        /// <summary>
        /// Gets the child elements.
        /// </summary>
        public IEnumerable<T> Children { get; }
    }
}