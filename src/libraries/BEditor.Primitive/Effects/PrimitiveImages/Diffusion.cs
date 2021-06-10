// Diffusion.cs
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
    /// Represents an effect that diffuses an image.
    /// </summary>
    public sealed class Diffusion : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Value"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Diffusion, EaseProperty> ValueProperty = EditingProperty.RegisterDirect<EaseProperty, Diffusion>(
            nameof(Value),
            owner => owner.Value,
            (owner, obj) => owner.Value = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.ThresholdValue, 7, 30, 0)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="Diffusion"/> class.
        /// </summary>
        public Diffusion()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Diffusion;

        /// <summary>
        /// Gets the threshold value.
        /// </summary>
        [AllowNull]
        public EaseProperty Value { get; set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            args.Value.Diffusion((byte)Value[args.Frame]);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Value;
        }
    }
}