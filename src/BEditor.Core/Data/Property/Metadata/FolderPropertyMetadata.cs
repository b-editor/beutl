// FolderPropertyMetadata.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="FolderProperty"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    /// <param name="Default">The default value of <see cref="FolderProperty.Value"/>.</param>
    public record FolderPropertyMetadata(string Name, string Default = "")
        : PropertyElementMetadata(Name), IEditingPropertyInitializer<FolderProperty>
    {
        /// <inheritdoc/>
        public FolderProperty Create()
        {
            return new(this);
        }
    }
}