// AudioObject.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

using BEditor.Audio;
using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Media;
using BEditor.Media.Decoding;
using BEditor.Media.PCM;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// Represents an <see cref="ObjectElement"/> that references an audio file.
    /// </summary>
    public sealed class AudioObject : ObjectElement
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
        /// Defines the <see cref="Start"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<AudioObject, ValueProperty> StartProperty = EditingProperty.RegisterDirect<ValueProperty, AudioObject>(
            nameof(Start),
            owner => owner.Start,
            (owner, obj) => owner.Start = obj,
            EditingPropertyOptions<ValueProperty>.Create(new ValuePropertyMetadata(Strings.Start + "(Milliseconds)", 0, Min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="File"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<AudioObject, FileProperty> FileProperty = EditingProperty.RegisterDirect<FileProperty, AudioObject>(
            nameof(File),
            owner => owner.File,
            (owner, obj) => owner.File = obj,
            EditingPropertyOptions<FileProperty>.Create(new FilePropertyMetadata(Strings.File, Filter: new(string.Empty, new FileExtension[] { new("mp3"), new("wav") }))).Serialize());

        /// <summary>
        /// Defines the <see cref="SetLength"/> property.
        /// </summary>
        public static readonly EditingProperty<ButtonComponent> SetLengthProperty = EditingProperty.Register<ButtonComponent, AudioObject>(
            nameof(SetLength),
            EditingPropertyOptions<ButtonComponent>.Create(new ButtonComponentMetadata(Strings.ClipLengthAsAudioLength)));

        private MediaFile? _mediaFile;

        private AudioSource? _source;

        private IDisposable? _disposable1;

        private IDisposable? _disposable2;

        /// <summary>
        /// Initializes a new instance of the <see cref="AudioObject"/> class.
        /// </summary>
        public AudioObject()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Audio;

        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the volume.
        /// </summary>
        [AllowNull]
        public EaseProperty Volume { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the pitch.
        /// </summary>
        [AllowNull]
        public EaseProperty Pitch { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the start position.
        /// </summary>
        [AllowNull]
        public ValueProperty Start { get; private set; }

        /// <summary>
        /// Get the <see cref="FileProperty"/> to select the file to reference.
        /// </summary>
        [AllowNull]
        public FileProperty File { get; private set; }

        /// <summary>
        /// Gets the command to set the length of the clip.
        /// </summary>
        public ButtonComponent SetLength => GetValue(SetLengthProperty);

        /// <summary>
        /// Gets the opened decoder.
        /// </summary>
        public MediaFile? Decoder
        {
            get => _mediaFile;
            set
            {
                _mediaFile?.Dispose();
                _mediaFile = value;

                if (_mediaFile is not null && _source is not null)
                {
                    _source.Buffer = new();
                    _source.Buffer?.Dispose();
                    Loaded?.Dispose();
                    Loaded = GetAllFrame(_mediaFile.Audio!);

                    _source.Buffer = new(Loaded);
                }
            }
        }

        /// <summary>
        /// Gets the loaded audio data.
        /// </summary>
        public Sound<StereoPCMFloat>? Loaded { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs args)
        {
            if (args.Type is not RenderType.VideoPreview) return;

            if (Decoder is null) return;

            _source!.Gain = Volume[args.Frame] / 100f;
            _source.Pitch = Pitch[args.Frame] / 100f;

            if (args.Frame == Parent.Start)
            {
                Task.Run(async () =>
                {
                    _source!.Play();

                    var millis = (int)Parent.Length.ToMilliseconds(Parent.Parent.Parent.Framerate);
                    await Task.Delay(millis);

                    StopIfPlaying();
                });
            }
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Volume;
            yield return Pitch;
            yield return Start;
            yield return File;
            yield return SetLength;
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            _source = new();

            _disposable1 = File.Where(file => System.IO.File.Exists(file)).Subscribe(file =>
            {
                Decoder = MediaFile.Open(file, new()
                {
                    StreamsToLoad = MediaMode.Audio,
                    SampleRate = this.GetRequiredParent<Project>().Samplingrate,
                });
            });

            _disposable2 = SetLength.Where(_ => Loaded is not null).Subscribe(_ =>
            {
                var length = Frame.FromTimeSpan(Loaded!.Duration, this.GetRequiredParent<Project>().Framerate);

                Parent.ChangeLength(Parent.Start, Parent.Start + length).Execute();
            });

            var player = Parent.Parent.Player;
            player.Stopped += Player_Stopped;

            player.Playing += Player_PlayingAsync;
        }

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            _disposable1?.Dispose();
            _disposable2?.Dispose();
            _source?.Dispose();
            _source = null;
            _mediaFile?.Dispose();
            _mediaFile = null;
            Loaded?.Dispose();
            Loaded = null;

            var player = Parent.Parent.Player;
            player.Stopped -= Player_Stopped;

            player.Playing -= Player_PlayingAsync;
        }

        private static Sound<StereoPCMFloat> GetAllFrame(IAudioStream stream)
        {
            return stream.GetFrame(TimeSpan.Zero, stream.Info.Duration);
        }

        private async void Player_PlayingAsync(object? sender, PlayingEventArgs e)
        {
            if (Parent.Start <= e.StartFrame && e.StartFrame <= Parent.End && Decoder is not null)
            {
                var framerate = Parent.Parent.Parent.Framerate;
                var startmsec = e.StartFrame.ToMilliseconds(framerate);
                var hStart = startmsec - Parent.Start.ToMilliseconds(framerate);
                var lengthMsec = (int)(Parent.Length.ToMilliseconds(framerate) - hStart);

                _source!.SecOffset = (float)TimeSpan.FromMilliseconds(Start.Value + startmsec).TotalSeconds;

                _source.Play();

                await Task.Delay(lengthMsec);

                StopIfPlaying();
            }
        }

        private void Player_Stopped(object? sender, EventArgs e)
        {
            StopIfPlaying();
        }

        private void StopIfPlaying()
        {
            if (_source is null) return;

            if (_source.State is AudioSourceState.Playing)
            {
                _source.Stop();
            }
        }
    }
}