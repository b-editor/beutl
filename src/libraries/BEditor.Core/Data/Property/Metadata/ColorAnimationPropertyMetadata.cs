// ColorAnimationPropertyMetadata.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Data.Property.Easing;
using BEditor.Drawing;

namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="ColorAnimationProperty"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    /// <param name="DefaultColor">The default color.</param>
    /// <param name="DefaultEase">The default easing function.</param>
    public record ColorAnimationPropertyMetadata(string Name, Color DefaultColor, EasingMetadata DefaultEase)
        : PropertyElementMetadata(Name), IEditingPropertyInitializer<ColorAnimationProperty>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ColorAnimationPropertyMetadata"/> class.
        /// </summary>
        /// <param name="name">The string displayed in the property header.</param>
        public ColorAnimationPropertyMetadata(string name)
            : this(name, default, EasingMetadata.GetDefault())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ColorAnimationPropertyMetadata"/> class.
        /// </summary>
        /// <param name="name">The string displayed in the property header.</param>
        /// <param name="defaultColor">The default color.</param>
        public ColorAnimationPropertyMetadata(string name, Color defaultColor)
            : this(name, defaultColor, EasingMetadata.GetDefault())
        {
        }

        /// <inheritdoc/>
        public ColorAnimationProperty Create()
        {
            return new(this);
        }
    }
}