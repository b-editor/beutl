// Noise.cs
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
    /// Noise effect.
    /// </summary>
    public sealed class Noise : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="FrequencyX"/> property.
        /// </summary>
        public static readonly DirectProperty<Noise, EaseProperty> FrequencyXProperty = EditingProperty.RegisterDirect<EaseProperty, Noise>(
            nameof(FrequencyX),
            owner => owner.FrequencyX,
            (owner, obj) => owner.FrequencyX = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.FrequencyX, 1, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="FrequencyY"/> property.
        /// </summary>
        public static readonly DirectProperty<Noise, EaseProperty> FrequencyYProperty = EditingProperty.RegisterDirect<EaseProperty, Noise>(
            nameof(FrequencyY),
            owner => owner.FrequencyY,
            (owner, obj) => owner.FrequencyY = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.FrequencyY, 1, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="NumberOfOctaves"/> property.
        /// </summary>
        public static readonly DirectProperty<Noise, EaseProperty> NumberOfOctavesProperty = EditingProperty.RegisterDirect<EaseProperty, Noise>(
            nameof(NumberOfOctaves),
            owner => owner.NumberOfOctaves,
            (owner, obj) => owner.NumberOfOctaves = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.NumberOfOctaves, 1, min: 1)).Serialize());

        /// <summary>
        /// Defines the <see cref="Type"/> property.
        /// </summary>
        public static readonly DirectProperty<Noise, SelectorProperty> TypeProperty = EditingProperty.RegisterDirect<SelectorProperty, Noise>(
            nameof(Type),
            owner => owner.Type,
            (owner, obj) => owner.Type = obj,
            EditingPropertyOptions<SelectorProperty>.Create(new SelectorPropertyMetadata(Strings.Type, new string[] { Strings.Fractal, Strings.Turbulence })).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="Noise"/> class.
        /// </summary>
        public Noise()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Noise;

        /// <summary>
        /// Gets the frequency in the x-direction.
        /// </summary>
        [AllowNull]
        public EaseProperty FrequencyX { get; private set; }

        /// <summary>
        /// Gets the frequency in the y-direction.
        /// </summary>
        [AllowNull]
        public EaseProperty FrequencyY { get; private set; }

        /// <summary>
        /// Gets the number of octaves.
        /// </summary>
        [AllowNull]
        public EaseProperty NumberOfOctaves { get; private set; }

        /// <summary>
        /// Gets the type of noise.
        /// </summary>
        [AllowNull]
        public SelectorProperty Type { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            var f = args.Frame;
            var freqX = FrequencyX[f] / 100f;
            var freqY = FrequencyY[f] / 100f;
            var numOctaves = (int)NumberOfOctaves[f];

            if (Type.Index == 0)
            {
                args.Value.FractalNoise(freqX, freqY, numOctaves, f.Value);
            }
            else
            {
                args.Value.TurbulenceNoise(freqX, freqY, numOctaves, f.Value);
            }
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return FrequencyX;
            yield return FrequencyY;
            yield return NumberOfOctaves;
            yield return Type;
        }
    }
}