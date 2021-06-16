// AudioObject.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Diagnostics.CodeAnalysis;
using System.Linq;

using BEditor.Data.Property;
using BEditor.Media;
using BEditor.Media.PCM;
using BEditor.Resources;

namespace BEditor.Data.Primitive
{
    /// <summary>
    /// Represents the base class of the object that samples the audio.
    /// </summary>
    public abstract class AudioObject : ObjectElement
    {
        /// <summary>
        /// Defines the <see cref="Volume"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<AudioObject, EaseProperty> VolumeProperty = EditingProperty.RegisterDirect<EaseProperty, AudioObject>(
            nameof(Volume),
            owner => owner.Volume,
            (owner, obj) => owner.Volume = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Volume, 50, float.NaN, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Pitch"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<AudioObject, EaseProperty> PitchProperty = EditingProperty.RegisterDirect<EaseProperty, AudioObject>(
            nameof(Pitch),
            owner => owner.Pitch,
            (owner, obj) => owner.Pitch = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Pitch, 100, 200, 50)).Serialize());

        /// <summary>
        /// Get the volume.
        /// </summary>
        [AllowNull]
        public EaseProperty Volume { get; private set; }

        /// <summary>
        /// Get the pitch.
        /// </summary>
        [AllowNull]
        public EaseProperty Pitch { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs args)
        {
            if (args.Type is not ApplyType.Audio) return;

            var f = args.Frame;
            var context = this.GetRequiredParent<Scene>().SamplingContext!;
            var sound = OnSample(args);
            if (sound is not null)
            {
                sound.Gain(Volume[f] / 100);

                var args2 = new EffectApplyArgs<Sound<StereoPCMFloat>>(f, sound, args.Type);
                foreach (var item in Parent.Effect.Skip(1).Where(i => i.IsEnabled))
                {
                    if (item is AudioEffect audioEffect)
                    {
                        audioEffect.Apply(args2);
                    }
                }

                if (!args2.Value.IsDisposed)
                {
                    context.Combine(args2.Value);
                }

                args2.Value.Dispose();
                sound.Dispose();
            }
        }

        /// <inheritdoc cref="Apply(EffectApplyArgs)"/>
        protected abstract Sound<StereoPCMFloat>? OnSample(EffectApplyArgs args);
    }
}