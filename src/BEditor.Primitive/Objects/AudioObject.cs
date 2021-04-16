using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading.Tasks;

using BEditor.Audio;
using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Media;
using BEditor.Media.Decoder;
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
        /// Represents <see cref="Coordinate"/> metadata.
        /// </summary>
        public static readonly PropertyElementMetadata CoordinateMetadata = ImageObject.CoordinateMetadata;
        /// <summary>
        /// Represents <see cref="Volume"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata VolumeMetadata = new(Strings.Volume, 50, 100, 0);
        /// <summary>
        /// Represents <see cref="Pitch"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata PitchMetadata = new(Strings.Pitch, 100, 200, 50);
        /// <summary>
        /// Represents <see cref="Start"/> metadata.
        /// </summary>
        public static readonly ValuePropertyMetadata StartMetadata = new(Strings.Start + "(Milliseconds)", 0, Min: 0);
        /// <summary>
        /// Represens <see cref="File"/> metadata.
        /// </summary>
        public static readonly FilePropertyMetadata FileMetadata = VideoFile.FileMetadata with { Filter = new("", new FileExtension[] { new("mp3"), new("wav") }) };
        private FFmpegDecoder? _decoder;
        private AudioSource? _source;
        private IDisposable? _disposable;

        /// <summary>
        /// Initializes a new instance of <see cref="AudioObject"/> class.
        /// </summary>
        public AudioObject()
        {
            Coordinate = new(CoordinateMetadata);
            Volume = new(VolumeMetadata);
            Pitch = new(PitchMetadata);
            Start = new(StartMetadata);
            File = new(FileMetadata);
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
        [DataMember]
        public AudioCoordinate Coordinate { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the volume.
        /// </summary>
        [DataMember]
        public EaseProperty Volume { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the pitch.
        /// </summary>
        [DataMember]
        public EaseProperty Pitch { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the start position.
        /// </summary>
        [DataMember]
        public ValueProperty Start { get; private set; }
        /// <summary>
        /// Get the <see cref="FileProperty"/> to select the file to reference.
        /// </summary>
        [DataMember]
        public FileProperty File { get; private set; }
        private FFmpegDecoder? Decoder
        {
            get
            {
                if (_decoder is null && System.IO.File.Exists(File.Value))
                {
                    Decoder = new(File.Value);
                }

                return _decoder;
            }
            set
            {
                _decoder?.Dispose();
                _decoder = value;

                if (_decoder is not null && _source is not null)
                {
                    _source.Buffer?.Dispose();
                    _decoder.ReadAll(out Sound<StereoPCM16> sound);

                    _source.Buffer = new(sound);

                    sound.Dispose();
                }
            }
        }

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
            Coordinate.Load(CoordinateMetadata);
            Volume.Load(VolumeMetadata);
            Pitch.Load(PitchMetadata);
            Start.Load(StartMetadata);
            File.Load(FileMetadata);

            _source = new();

            _disposable = File.Where(file => System.IO.File.Exists(file)).Subscribe(file =>
            {
                Decoder = new(file);
            });

            var player = Parent.Parent.Player;
            player.Stopped += Player_Stopped;

            player.Playing += Player_PlayingAsync;
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
            _decoder?.Dispose();
            _decoder = null;

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
            /// Represents <see cref="X"/> metadata.
            /// </summary>
            public static readonly EasePropertyMetadata XMetadata = Data.Property.PrimitiveGroup.Coordinate.XMetadata;
            /// <summary>
            /// Represents <see cref="Y"/> metadata.
            /// </summary>
            public static readonly EasePropertyMetadata YMetadata = Data.Property.PrimitiveGroup.Coordinate.YMetadata;
            /// <summary>
            /// Represents <see cref="Z"/> metadata.
            /// </summary>
            public static readonly EasePropertyMetadata ZMetadata = Data.Property.PrimitiveGroup.Coordinate.ZMetadata;
            /// <summary>
            /// Represents <see cref="DirectionX"/> metadata.
            /// </summary>
            public static readonly EasePropertyMetadata DirectionXMetadata = Data.Property.PrimitiveGroup.Coordinate.XMetadata with { Name = "Direction x" };
            /// <summary>
            /// Represents <see cref="DirectionY"/> metadata.
            /// </summary>
            public static readonly EasePropertyMetadata DirectionYMetadata = Data.Property.PrimitiveGroup.Coordinate.YMetadata with { Name = "Direction y" };
            /// <summary>
            /// Represents <see cref="DirectionZ"/> metadata.
            /// </summary>
            public static readonly EasePropertyMetadata DirectionZMetadata = Data.Property.PrimitiveGroup.Coordinate.ZMetadata with { Name = "Direction z" };

            /// <summary>
            /// Initializes a new instance of the <see cref="AudioCoordinate"/> class.
            /// </summary>
            /// <param name="metadata">Metadata of this property.</param>
            /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
            public AudioCoordinate(PropertyElementMetadata metadata) : base(metadata)
            {
                X = new(XMetadata);
                Y = new(YMetadata);
                Z = new(ZMetadata);
                DirectionX = new(DirectionXMetadata);
                DirectionY = new(DirectionYMetadata);
                DirectionZ = new(DirectionZMetadata);
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
            [DataMember]
            public EaseProperty X { get; private set; }
            /// <summary>
            /// Get the <see cref="EaseProperty"/> representing the Y coordinate.
            /// </summary>
            [DataMember]
            public EaseProperty Y { get; private set; }
            /// <summary>
            /// Get the <see cref="EaseProperty"/> representing the Z coordinate.
            /// </summary>
            [DataMember]
            public EaseProperty Z { get; private set; }
            /// <summary>
            /// Get the <see cref="EaseProperty"/> representing the X coordinate.
            /// </summary>
            [DataMember]
            public EaseProperty DirectionX { get; private set; }
            /// <summary>
            /// Get the <see cref="EaseProperty"/> representing the Y coordinate.
            /// </summary>
            [DataMember]
            public EaseProperty DirectionY { get; private set; }
            /// <summary>
            /// Get the <see cref="EaseProperty"/> representing the Z coordinate.
            /// </summary>
            [DataMember]
            public EaseProperty DirectionZ { get; private set; }

            /// <inheritdoc/>
            protected override void OnLoad()
            {
                X.Load(XMetadata);
                Y.Load(YMetadata);
                Z.Load(ZMetadata);
                DirectionX.Load(DirectionXMetadata);
                DirectionY.Load(DirectionYMetadata);
                DirectionZ.Load(DirectionZMetadata);
            }
            /// <inheritdoc/>
            protected override void OnUnload()
            {
                X.Unload();
                Y.Unload();
                Z.Unload();
                DirectionX.Unload();
                DirectionY.Unload();
                DirectionZ.Unload();
            }
        }
    }
}