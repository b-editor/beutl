// ColorKey.cs
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
    /// Represents the ColorKey effect.
    /// </summary>
    public sealed class ColorKey : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<ColorKey, ColorProperty> ColorProperty = EditingProperty.RegisterDirect<ColorProperty, ColorKey>(
            nameof(Color),
            owner => owner.Color,
            (owner, obj) => owner.Color = obj,
            EditingPropertyOptions<ColorProperty>.Create(new ColorPropertyMetadata(Strings.Color, Colors.White)).Serialize());

        /// <summary>
        /// Defines the <see cref="ThresholdValue"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<ColorKey, EaseProperty> ThresholdValueProperty = EditingProperty.RegisterDirect<EaseProperty, ColorKey>(
            nameof(ThresholdValue),
            owner => owner.ThresholdValue,
            (owner, obj) => owner.ThresholdValue = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.ThresholdValue, 60)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="ColorKey"/> class.
        /// </summary>
        public ColorKey()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.ColorKey;

        /// <summary>
        /// Gets the key color.
        /// </summary>
        [AllowNull]
        public ColorProperty Color { get; private set; }

        /// <summary>
        /// Gets the threshold value.
        /// </summary>
        [AllowNull]
        public EaseProperty ThresholdValue { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            args.Value.ColorKey(Color.Value, (int)ThresholdValue[args.Frame]);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Color;
            yield return ThresholdValue;
        }
    }
}