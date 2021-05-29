// IChild.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

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