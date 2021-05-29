// Border.cs
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
    /// Represents the <see cref="ImageEffect"/> that adds a border to the image.
    /// </summary>
    public sealed class Border : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Size"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Border, EaseProperty> SizeProperty = EditingProperty.RegisterDirect<EaseProperty, Border>(
            nameof(Size),
            owner => owner.Size,
            (owner, obj) => owner.Size = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Size, 10, float.NaN, 1)).Serialize());

        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Border, ColorProperty> ColorProperty = EditingProperty.RegisterDirect<ColorProperty, Border>(
            nameof(Color),
            owner => owner.Color,
            (owner, obj) => owner.Color = obj,
            EditingPropertyOptions<ColorProperty>.Create(new ColorPropertyMetadata(Strings.Color, Colors.White)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="Border"/> class.
        /// </summary>
        public Border()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Border;

        /// <summary>
        /// Gets the size of the edge.
        /// </summary>
        [AllowNull]
        public EaseProperty Size { get; private set; }

        /// <summary>
        /// Gets the border color.
        /// </summary>
        [AllowNull]
        public ColorProperty Color { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            var img = args.Value.Border((int)Size!.GetValue(args.Frame), Color!.Value);
            args.Value.Dispose();

            args.Value = img;
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Size;
            yield return Color;
        }
    }
}