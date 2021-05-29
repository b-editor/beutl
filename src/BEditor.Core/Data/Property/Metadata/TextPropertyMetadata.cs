// TextPropertyMetadata.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="TextProperty"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    /// <param name="DefaultText">The default value for <see cref="TextProperty.Value"/>.</param>
    public record TextPropertyMetadata(string Name, string DefaultText = "")
        : PropertyElementMetadata(Name), IEditingPropertyInitializer<TextProperty>
    {
        /// <inheritdoc/>
        public TextProperty Create()
        {
            return new(this);
        }
    }
}