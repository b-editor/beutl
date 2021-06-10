// ColorAnimationPropertyMetadata.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

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
    /// <param name="UseAlpha">The value of whether to use alpha components or not.</param>
    public record ColorAnimationPropertyMetadata(string Name, Color DefaultColor, EasingMetadata DefaultEase, bool UseAlpha = false)
        : PropertyElementMetadata(Name), IEditingPropertyInitializer<ColorAnimationProperty>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ColorAnimationPropertyMetadata"/> class.
        /// </summary>
        /// <param name="name">The string displayed in the property header.</param>
        public ColorAnimationPropertyMetadata(string name)
            : this(name, default, EasingMetadata.LoadedEasingFunc[0])
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ColorAnimationPropertyMetadata"/> class.
        /// </summary>
        /// <param name="name">The string displayed in the property header.</param>
        /// <param name="defaultColor">The default color.</param>
        /// <param name="useAlpha">The value of whether to use alpha components or not.</param>
        public ColorAnimationPropertyMetadata(string name, Color defaultColor, bool useAlpha = false)
            : this(name, defaultColor, EasingMetadata.LoadedEasingFunc[0], useAlpha)
        {
        }

        /// <inheritdoc/>
        public ColorAnimationProperty Create()
        {
            return new(this);
        }
    }
}