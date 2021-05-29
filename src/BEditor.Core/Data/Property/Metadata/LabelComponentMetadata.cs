// LabelComponentMetadata.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="LabelComponent"/>.
    /// </summary>
    public record LabelComponentMetadata()
        : PropertyElementMetadata(string.Empty), IEditingPropertyInitializer<LabelComponent>
    {
        /// <inheritdoc/>
        public LabelComponent Create()
        {
            return new();
        }
    }
}