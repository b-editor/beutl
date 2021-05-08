using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            new EasePropertyMetadata(Strings.Volume, 50, 100, 0));

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

        private MediaFile? _mediaFile;

        private AudioSource? _source;

        private IDisposable? _disposable;

        /// <summary>
        /// Initializes a new instance of <see cref="AudioObject"/> class.
        /// </summary>
#pragma warning disable CS8618
        public AudioObject()
#pragma warning restore CS8618
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Audio;

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Coordinate,
            Volume,
            Pitch,
            Start,
            File
        };

        /// <summary>
        /// Get the coordinates.
        /// </summary>
        public AudioCoordinate Coordinate { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the volume.
        /// </summary>
        public EaseProperty Volume { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the pitch.
        /// </summary>
        public EaseProperty Pitch { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the start position.
        /// </summary>
        public ValueProperty Start { get; private set; }

        /// <summary>
        /// Get the <see cref="FileProperty"/> to select the file to reference.
        /// </summary>
        public FileProperty File { get; private set; }

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
                    _source.Buffer = new(0);
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
        public override void Render(EffectRenderArgs args)
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
        protected override void OnLoad()
        {
            _source = new();

            _disposable = File.Where(file => System.IO.File.Exists(file)).Subscribe(async file =>
            {
                if (Path.GetExtension(file) is ".mp3")
                {
                    Decoder = MediaFile.Open(file, new()
                    {
                        StreamsToLoad = MediaMode.Audio,
                    });

                    return;
                }

                // 強制的に44100hz SinglePに変更
                var exe = FFmpegLoader.GetExecutable();
                var proj = this.GetParentRequired<Project>();
                var contentDir = Path.Combine(proj.DirectoryName, "content");
                var dst = Path.Combine(contentDir, ID.ToString() + ".mp3");

                if (!Directory.Exists(contentDir)) Directory.CreateDirectory(contentDir);
                if (System.IO.File.Exists(dst)) System.IO.File.Delete(dst);

                var process = Process.Start(exe, $"-i {file} -vcodec copy -ar {proj.Samplingrate} {dst}");
                await process.WaitForExitAsync();
                process.Dispose();

                Decoder = MediaFile.Open(dst, new()
                {
                    StreamsToLoad = MediaMode.Audio,
                });
            });

            var player = Parent.Parent.Player;
            player.Stopped += Player_Stopped;

            player.Playing += Player_PlayingAsync;
        }

        private static Sound<StereoPCMFloat> GetAllFrame(AudioStream stream)
        {
            stream.TryGetFrame(TimeSpan.Zero, out _);
            var sampleL = new List<float>();
            var sampleR = new List<float>();

            while (stream.TryGetNextFrame(out var audio))
            {
                var array = audio.GetSampleData();

                sampleL.AddRange(array[0]);

                if (array.Length is 2)
                {
                    sampleR.AddRange(array[1]);
                }
            }

            var sound = new Sound<StereoPCMFloat>(stream.Info.SampleRate, sampleL.Count);

            if (sampleR.Count is 0)
            {
                sampleL.Select(i => new StereoPCMFloat(i, i)).ToArray().CopyTo(sound.Data);
            }
            else
            {
                sampleL.Zip(sampleR, (l, r) => new StereoPCMFloat(l, r))
                    .ToArray()
                    .CopyTo(sound.Data);
            }

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

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            _disposable?.Dispose();
            _source?.Dispose();
            _source = null;
            _mediaFile?.Dispose();
            _mediaFile = null;

            var player = Parent.Parent.Player;
            player.Stopped -= Player_Stopped;

            player.Playing -= Player_PlayingAsync;
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

            /// <inheritdoc/>
            public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
            {
                X,
                Y,
                Z,
                DirectionX,
                DirectionY,
                DirectionZ,
            };

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