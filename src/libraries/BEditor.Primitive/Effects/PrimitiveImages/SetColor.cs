// SetColor.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an <see cref="ImageEffect"/> that monochromatizes an image.
    /// </summary>
    public sealed class SetColor : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<SetColor, ColorProperty> ColorProperty = ColorKey.ColorProperty.WithOwner<SetColor>(
            owner => owner.Color,
            (owner, obj) => owner.Color = obj);

        /// <summary>
        /// Initializes a new instance of the <see cref="SetColor"/> class.
        /// </summary>
        public SetColor()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Monoc;

        /// <summary>
        /// Gets the color to be monochromatic.
        /// </summary>
        [AllowNull]
        public ColorProperty Color { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            args.Value.SetColor(Color.Value);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Color;
        }
    }
}