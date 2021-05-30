// IHasMetadata.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Data
{
    /// <summary>
    /// Represents an object with metadata.
    /// </summary>
    public interface IHasMetadata
    {
        /// <summary>
        /// Gets or sets the metadata.
        /// </summary>
        public object? Metadata { get; set; }
    }
}