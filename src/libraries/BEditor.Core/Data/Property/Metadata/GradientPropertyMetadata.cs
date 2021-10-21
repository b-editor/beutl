// GradientPropertyMetadata.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using BEditor.Drawing;

namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="ColorAnimationProperty"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    /// <param name="Color1">The first color.</param>
    /// <param name="Color2">The second color.</param>
    public record GradientPropertyMetadata(string Name, Color Color1, Color Color2)
        : PropertyElementMetadata(Name), IEditingPropertyInitializer<GradientProperty>
    {
        /// <inheritdoc/>
        public GradientProperty Create()
        {
            return new(this);
        }
    }
}
