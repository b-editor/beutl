// Binarization.cs
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
    /// Represents the <see cref="ImageEffect"/> of binarizing an image.
    /// </summary>
    public sealed class Binarization : ImageEffect
    {
        /// <summary>
        /// Difines the <see cref="Value"/> property.
        /// </summary>
        public static readonly DirectProperty<Binarization, EaseProperty> ValueProperty = EditingProperty.RegisterDirect<EaseProperty, Binarization>(
            nameof(Value),
            owner => owner.Value,
            (owner, obj) => owner.Value = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.ThresholdValue, 127, 255, 0)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="Binarization"/> class.
        /// </summary>
        public Binarization()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Binarization;

        /// <summary>
        /// Gets the threshold value.
        /// </summary>
        [AllowNull]
        public EaseProperty Value { get; set; }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Value;
        }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            args.Value.Binarization((byte)Value[args.Frame], Parent.Parent.DrawingContext);
        }
    }
}