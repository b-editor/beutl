// RGBColor.cs
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
    /// Represents an effect that corrects the RGB color tone of an image.
    /// </summary>
    public sealed class RGBColor : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Red"/> property.
        /// </summary>
        public static readonly DirectProperty<RGBColor, EaseProperty> RedProperty = EditingProperty.RegisterDirect<EaseProperty, RGBColor>(
            nameof(Red),
            owner => owner.Red,
            (owner, obj) => owner.Red = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Red, 0, 255, -255)).Serialize());

        /// <summary>
        /// Defines the <see cref="Green"/> property.
        /// </summary>
        public static readonly DirectProperty<RGBColor, EaseProperty> GreenProperty = EditingProperty.RegisterDirect<EaseProperty, RGBColor>(
            nameof(Green),
            owner => owner.Green,
            (owner, obj) => owner.Green = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Green, 0, 255, -255)).Serialize());

        /// <summary>
        /// Defines the <see cref="Blue"/> property.
        /// </summary>
        public static readonly DirectProperty<RGBColor, EaseProperty> BlueProperty = EditingProperty.RegisterDirect<EaseProperty, RGBColor>(
            nameof(Blue),
            owner => owner.Blue,
            (owner, obj) => owner.Blue = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Blue, 0, 255, -255)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="RGBColor"/> class.
        /// </summary>
        public RGBColor()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.RGBColorCorrection;

        /// <summary>
        /// Gets the red threshold value.
        /// </summary>
        [AllowNull]
        public EaseProperty Red { get; set; }

        /// <summary>
        /// Gets the green threshold value.
        /// </summary>
        [AllowNull]
        public EaseProperty Green { get; set; }

        /// <summary>
        /// Gets the blue threshold value.
        /// </summary>
        [AllowNull]
        public EaseProperty Blue { get; set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            args.Value.RGBColor(
                (short)Red[args.Frame],
                (short)Green[args.Frame],
                (short)Blue[args.Frame],
                Parent.Parent.DrawingContext);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Red;
            yield return Green;
            yield return Blue;
        }
    }
}