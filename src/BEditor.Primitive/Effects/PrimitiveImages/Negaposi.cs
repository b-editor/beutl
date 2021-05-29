// Negaposi.cs
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
    /// Represents the effect of flipping an image negative-positive.
    /// </summary>
    public sealed class Negaposi : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Red"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Negaposi, EaseProperty> RedProperty = EditingProperty.RegisterDirect<EaseProperty, Negaposi>(
            nameof(Red),
            owner => owner.Red,
            (owner, obj) => owner.Red = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Red, 255, 255, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Green"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Negaposi, EaseProperty> GreenProperty = EditingProperty.RegisterDirect<EaseProperty, Negaposi>(
            nameof(Green),
            owner => owner.Green,
            (owner, obj) => owner.Green = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Green, 255, 255, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Blue"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Negaposi, EaseProperty> BlueProperty = EditingProperty.RegisterDirect<EaseProperty, Negaposi>(
            nameof(Blue),
            owner => owner.Blue,
            (owner, obj) => owner.Blue = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Blue, 255, 255, 0)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="Negaposi"/> class.
        /// </summary>
        public Negaposi()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Negaposi;

        /// <summary>
        /// Gets the red.
        /// </summary>
        [AllowNull]
        public EaseProperty Red { get; private set; }

        /// <summary>
        /// Gets the green.
        /// </summary>
        [AllowNull]
        public EaseProperty Green { get; private set; }

        /// <summary>
        /// Gets the blue.
        /// </summary>
        [AllowNull]
        public EaseProperty Blue { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            args.Value.Negaposi(
                (byte)Red[args.Frame],
                (byte)Green[args.Frame],
                (byte)Blue[args.Frame],
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