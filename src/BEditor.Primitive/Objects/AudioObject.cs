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
        /// Defines the <see cref="Coordinate"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<AudioObject, AudioCoordinate> CoordinateProperty = EditingProperty.RegisterSerializeDirect<AudioCoordinate, AudioObject>(
            nameof(Coordinate),
            owner => owner.Coordinate,
            (owner, obj) => owner.Coordinate = obj,
            new AudioCoordinateMetadata(Strings.Coordinate));

        /// <summary>
        /// Defines the <see cref="Volume"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<AudioObject, EaseProperty> VolumeProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, AudioObject>(
            nameof(Volume),
            owner => owner.Volume,
            (owner, obj) => owner.Volume = obj,
            new EasePropertyMetadata(Strings.Volume, 50, float.NaN, 0));

        /// <summary>
        /// Defines the <see cref="Pitch"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<AudioObject, EaseProperty> PitchProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, AudioObject>(
            nameof(Pitch),
            owner => owner.Pitch,
            (owner, obj) => owner.Pitch = obj,
            new EasePropertyMetadata(Strings.Pitch, 100, 200, 50));

        /// <summary>
        /// Defines the <see cref="Start"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<AudioObject, ValueProperty> StartProperty = EditingProperty.RegisterSerializeDirect<ValueProperty, AudioObject>(
            nameof(Start),
            owner => owner.Start,
            (owner, obj) => owner.Start = obj,
            new ValuePropertyMetadata(Strings.Start + "(Milliseconds)", 0, Min: 0));

        /// <summary>
        /// Defines the <see cref="File"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<AudioObject, FileProperty> FileProperty = EditingProperty.RegisterSerializeDirect<FileProperty, AudioObject>(
            nameof(File),
            owner => owner.File,
            (owner, obj) => owner.File = obj,
            new FilePropertyMetadata(Strings.File, Filter: new("", new FileExtension[] { new("mp3"), new("wav") })));

        /// <summary>
        /// Defines the <see cref="SetLength"/> property.
        /// </summary>
        public static readonly EditingProperty<ButtonComponent> SetLengthProperty = EditingProperty.Register<ButtonComponent, AudioObject>(
            nameof(SetLength),
            new ButtonComponentMetadata(Strings.ClipLengthAsAudioLength));

        private MediaFile? _mediaFile;

        private AudioSource? _source;

        private IDisposable? _disposable1;

        private IDisposable? _disposable2;

        /// <summary>
        /// Initializes a new instance of <see cref="AudioObject"/> class.
        /// </summary>
        public AudioObject()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Audio;

        /// <summary>
        /// Get the coordinates.
        /// </summary>
        [AllowNull]
        public AudioCoordinate Coordinate { get; private set; }

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
        /// 
        /// </summary>
        public ButtonComponent SetLength => GetValue(SetLengthProperty);

        /// <summary>
        /// 
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
        ///
        /// </summary>
        public Sound<StereoPCMFloat>? Loaded { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs args)
        {
            if (args.Type is not RenderType.VideoPreview) return;

            if (Decoder is null) return;

            _source!.Gain = Volume[args.Frame] / 100f;
            _source.Position = new(Coordinate.X[args.Frame], Coordinate.Y[args.Frame], Coordinate.Z[args.Frame]);
            _source.Direction = new(Coordinate.DirectionX[args.Frame], Coordinate.DirectionY[args.Frame], Coordinate.DirectionZ[args.Frame]);
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
            yield return Coordinate;
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
                    SampleRate = this.GetParentRequired<Project>().Samplingrate
                });
            });

            _disposable2 = SetLength.Where(_ => Loaded is not null).Subscribe(_ =>
            {
                var length = Frame.FromTimeSpan(Loaded!.Time, this.GetParentRequired<Project>().Framerate);

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
            stream.TryGetFrame(TimeSpan.Zero, out _);
            var sampleL = new List<float>();
            var sampleR = new List<float>();

            while (stream.TryGetNextFrame(out var audio))
            {
                var array = audio.Extract();

                sampleL.AddRange(array[0]);

                if (array.Length is 2)
                {
                    sampleR.AddRange(array[1]);
                }
                else
                {
                    sampleR.AddRange(array[0]);
                }
            }

            var sound = new Sound<StereoPCMFloat>(stream.Info.SampleRate, sampleL.Count);

            sampleL.Zip(sampleR, (l, r) => new StereoPCMFloat(l, r))
                .ToArray()
                .CopyTo(sound.Data);

            return sound;
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

        /// <summary>
        /// Represents a property for setting XYZ coordinates.
        /// </summary>
        public sealed class AudioCoordinate : ExpandGroup
        {
            /// <summary>
            /// Defines the <see cref="X"/> property.
            /// </summary>
            public static readonly DirectEditingProperty<AudioCoordinate, EaseProperty> XProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, AudioCoordinate>(
                nameof(X),
                owner => owner.X,
                (owner, obj) => owner.X = obj,
                new EasePropertyMetadata(Strings.X, 0));

            /// <summary>
            /// Defines the <see cref="Y"/> property.
            /// </summary>
            public static readonly DirectEditingProperty<AudioCoordinate, EaseProperty> YProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, AudioCoordinate>(
                nameof(Y),
                owner => owner.Y,
                (owner, obj) => owner.Y = obj,
                new EasePropertyMetadata(Strings.Y, 0));

            /// <summary>
            /// Defines the <see cref="Z"/> property.
            /// </summary>
            public static readonly DirectEditingProperty<AudioCoordinate, EaseProperty> ZProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, AudioCoordinate>(
                nameof(Z),
                owner => owner.Z,
                (owner, obj) => owner.Z = obj,
                new EasePropertyMetadata(Strings.Z, 0));

            /// <summary>
            /// Defines the <see cref="DirectionX"/> property.
            /// </summary>
            public static readonly DirectEditingProperty<AudioCoordinate, EaseProperty> CenterXProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, AudioCoordinate>(
                nameof(DirectionX),
                owner => owner.DirectionX,
                (owner, obj) => owner.DirectionX = obj,
                new EasePropertyMetadata("Direction x", 0, float.NaN, float.NaN, true));

            /// <summary>
            /// Defines the <see cref="DirectionY"/> property.
            /// </summary>
            public static readonly DirectEditingProperty<AudioCoordinate, EaseProperty> CenterYProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, AudioCoordinate>(
                nameof(DirectionY),
                owner => owner.DirectionY,
                (owner, obj) => owner.DirectionY = obj,
                new EasePropertyMetadata("Direction y", 0, float.NaN, float.NaN, true));

            /// <summary>
            /// Defines the <see cref="DirectionZ"/> property.
            /// </summary>
            public static readonly DirectEditingProperty<AudioCoordinate, EaseProperty> CenterZProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, AudioCoordinate>(
                nameof(DirectionZ),
                owner => owner.DirectionZ,
                (owner, obj) => owner.DirectionZ = obj,
                new EasePropertyMetadata("Direction z", 0, float.NaN, float.NaN, true));

            /// <summary>
            /// Initializes a new instance of the <see cref="AudioCoordinate"/> class.
            /// </summary>
            /// <param name="metadata">Metadata of this property.</param>
            /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
#pragma warning disable CS8618
            public AudioCoordinate(AudioCoordinateMetadata metadata) : base(metadata)
#pragma warning restore CS8618
            {
            }

            /// <summary>
            /// Get the <see cref="EaseProperty"/> representing the X coordinate.
            /// </summary>
            public EaseProperty X { get; private set; }

            /// <summary>
            /// Get the <see cref="EaseProperty"/> representing the Y coordinate.
            /// </summary>
            public EaseProperty Y { get; private set; }

            /// <summary>
            /// Get the <see cref="EaseProperty"/> representing the Z coordinate.
            /// </summary>
            public EaseProperty Z { get; private set; }

            /// <summary>
            /// Get the <see cref="EaseProperty"/> representing the X coordinate.
            /// </summary>
            public EaseProperty DirectionX { get; private set; }

            /// <summary>
            /// Get the <see cref="EaseProperty"/> representing the Y coordinate.
            /// </summary>
            public EaseProperty DirectionY { get; private set; }

            /// <summary>
            /// Get the <see cref="EaseProperty"/> representing the Z coordinate.
            /// </summary>
            public EaseProperty DirectionZ { get; private set; }

            /// <inheritdoc/>
            public override IEnumerable<PropertyElement> GetProperties()
            {
                yield return X;
                yield return Y;
                yield return Z;
                yield return DirectionX;
                yield return DirectionY;
                yield return DirectionZ;
            }
        }

        /// <summary>
        /// The metadata of <see cref="AudioCoordinate"/>.
        /// </summary>
        /// <param name="Name">The string displayed in the property header.</param>
        public record AudioCoordinateMetadata(string Name) : PropertyElementMetadata(Name), IEditingPropertyInitializer<AudioCoordinate>
        {
            /// <inheritdoc/>
            public AudioCoordinate Create()
            {
                return new(this);
            }
        }
    }
}