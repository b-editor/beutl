// DocumentPropertyMetadata.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="DocumentProperty"/>.
    /// </summary>
    /// <param name="DefaultText">The default value of <see cref="DocumentProperty.Value"/>.</param>
    public record DocumentPropertyMetadata(string DefaultText)
        : PropertyElementMetadata(string.Empty), IEditingPropertyInitializer<DocumentProperty>
    {
        /// <inheritdoc/>
        public DocumentProperty Create()
        {
            return new(this);
        }
    }
}