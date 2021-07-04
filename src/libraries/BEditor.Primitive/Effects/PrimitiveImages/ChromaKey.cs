// ChromaKey.cs
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
    /// Represents the <see cref="ImageEffect"/> that makes the background color of an image transparent.
    /// </summary>
    public sealed class ChromaKey : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectProperty<ChromaKey, ColorProperty> ColorProperty = ColorKey.ColorProperty.WithOwner<ChromaKey>(
            owner => owner.Color,
            (owner, obj) => owner.Color = obj);

        /// <summary>
        /// Defines the <see cref="HueRange"/> property.
        /// </summary>
        public static readonly DirectProperty<ChromaKey, EaseProperty> HueRangeProperty = EditingProperty.RegisterDirect<EaseProperty, ChromaKey>(
            nameof(HueRange),
            owner => owner.HueRange,
            (owner, obj) => owner.HueRange = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.HueRange, 80, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="SaturationRange"/> property.
        /// </summary>
        public static readonly DirectProperty<ChromaKey, EaseProperty> SaturationRangeProperty = EditingProperty.RegisterDirect<EaseProperty, ChromaKey>(
            nameof(SaturationRange),
            owner => owner.SaturationRange,
            (owner, obj) => owner.SaturationRange = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.SaturationRange, 80, min: 0)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="ChromaKey"/> class.
        /// </summary>
        public ChromaKey()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.ChromaKey;

        /// <summary>
        /// Gets the key color.
        /// </summary>
        [AllowNull]
        public ColorProperty Color { get; private set; }

        /// <summary>
        /// Gets the hue range.
        /// </summary>
        [AllowNull]
        public EaseProperty HueRange { get; private set; }

        /// <summary>
        /// Gets the saturation range.
        /// </summary>
        [AllowNull]
        public EaseProperty SaturationRange { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            args.Value.ChromaKey(Color.Value, (int)HueRange[args.Frame], (int)SaturationRange[args.Frame], Parent.Parent.DrawingContext);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Color;
            yield return HueRange;
            yield return SaturationRange;
        }
    }
}