// FilePropertyMetadata.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="FileProperty"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    /// <param name="DefaultFile">The default value of <see cref="FileProperty.Value"/>.</param>
    /// <param name="Filter">The filter for the file to be selected.</param>
    public record FilePropertyMetadata(string Name, string DefaultFile = "", FileFilter? Filter = null)
        : PropertyElementMetadata(Name), IEditingPropertyInitializer<FileProperty>
    {
        /// <inheritdoc/>
        public FileProperty Create()
        {
            return new(this);
        }
    }
}