// Delay.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Media;
using BEditor.Media.PCM;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an effect that delays audio.
    /// </summary>
    public sealed class Delay : AudioEffect
    {
        /// <summary>
        /// Defines the <see cref="Attenuation"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Delay, EaseProperty> AttenuationProperty = EditingProperty.RegisterDirect<EaseProperty, Delay>(
            nameof(Attenuation),
            o => o.Attenuation,
            (o, v) => o.Attenuation = v,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata("減衰率", 50, 100, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="DelayTime"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Delay, EaseProperty> DelayTimeProperty = EditingProperty.RegisterDirect<EaseProperty, Delay>(
            nameof(DelayTime),
            o => o.DelayTime,
            (o, v) => o.DelayTime = v,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata("遅延時間", 37, 100, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Repeat"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Delay, EaseProperty> RepeatProperty = EditingProperty.RegisterDirect<EaseProperty, Delay>(
            nameof(Repeat),
            o => o.Repeat,
            (o, v) => o.Repeat = v,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata("繰り返し回数", 5, 0, 10)).Serialize());

        /// <inheritdoc/>
        public override string Name => Strings.Delay;

        /// <summary>
        /// Gets the attenuation rate.
        /// </summary>
        [AllowNull]
        public EaseProperty Attenuation { get; private set; }

        /// <summary>
        /// Gets the delay time.
        /// </summary>
        [AllowNull]
        public EaseProperty DelayTime { get; private set; }

        /// <summary>
        /// Gets the number of repetitions.
        /// </summary>
        [AllowNull]
        public EaseProperty Repeat { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Sound<StereoPCMFloat>> args)
        {
            args.Value.Delay(
                Attenuation[args.Frame] / 100,
                DelayTime[args.Frame] / 100,
                (int)Repeat[args.Frame]);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Attenuation;
            yield return DelayTime;
            yield return Repeat;
        }
    }
}