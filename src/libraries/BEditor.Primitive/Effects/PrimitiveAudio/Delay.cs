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
            var sound = args.Value;
            using var src_ = sound.Clone();
            var src = src_.Data;
            var dst = sound.Data;
            var a = Attenuation[args.Frame] / 100; /* 減衰率 */
            var d = sound.SampleRate * DelayTime[args.Frame] / 100; /* 遅延時間 */
            var repeat = (int)Repeat[args.Frame]; /* 繰り返し回数 */

            for (var n = 0; n < sound.NumSamples; n++)
            {
                for (var i = 1; i <= repeat; i++)
                {
                    var m = (int)(n - (i * d));

                    if (m >= 0)
                    {
                        var value = MathF.Pow(a, i);
                        dst[n].Left += value * src[m].Left;
                        dst[n].Right += value * src[m].Right;
                    }
                }
            }
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
