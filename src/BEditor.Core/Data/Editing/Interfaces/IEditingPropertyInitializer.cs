// IEditingPropertyInitializer.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Data
{
    /// <summary>
    /// Represents the ability to create an instance of a local value of <see cref="EditingProperty"/>.
    /// </summary>
    public interface IEditingPropertyInitializer
    {
        /// <summary>
        /// Create a local value instance of <see cref="EditingProperty"/>.
        /// </summary>
        /// <returns>Returns the created local value.</returns>
        public object Create();
    }
}