// IElementObject.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Data
{
    /// <summary>
    /// Represents an element of the editing data.
    /// </summary>
    public interface IElementObject
    {
        /// <summary>
        /// Gets a value indicating whether or not this <see cref="IElementObject"/> has been loaded.
        /// </summary>
        public bool IsLoaded { get; }

        /// <summary>
        /// Activate this <see cref="IElementObject"/>.
        /// </summary>
        public void Load();

        /// <summary>
        /// Disables this <see cref="IElementObject"/>.
        /// </summary>
        public void Unload();
    }
}