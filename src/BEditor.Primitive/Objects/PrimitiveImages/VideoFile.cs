using System;
using System.Collections.Generic;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Media;
using BEditor.Media.Decoder;
using BEditor.Primitive.Resources;

using Microsoft.Extensions.DependencyInjection;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// Represents an <see cref="ImageObject"/> that references an video file.
    /// </summary>
    public sealed class VideoFile : ImageObject
    {
        /// <summary>
        /// Represents <see cref="Speed"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata SpeedMetadata = new(Strings.Speed, 100);
        /// <summary>
        /// Represents <see cref="Start"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata StartMetadata = new(Strings.Start, 1, float.NaN, 0);
        /// <summary>
        /// Represents <see cref="File"/> metadata.
        /// </summary>
        public static readonly FilePropertyMetadata FileMetadata = new(Strings.File, "", new(Strings.VideoFile, new FileExtension[]
        {
            new("mp4"),
            new("avi"),
            new("wmv"),
            new("mov")
        }));
        private IMediaDecoder? _videoReader;
        private IDisposable? _disposable;

        /// <summary>
        /// Initializes a new instance of the <see cref="VideoFile"/> class.
        /// </summary>
        public VideoFile()
        {
            Speed = new(SpeedMetadata);
            Start = new(StartMetadata);
            File = new(FileMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Strings.Video;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Coordinate,
            Scale,
            Blend,
            Rotate,
            Material,
            Speed,
            Start,
            File
        };
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the playback speed.
        /// </summary>
        [DataMember]
        public EaseProperty Speed { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the start position.
        /// </summary>
        [DataMember]
        public EaseProperty Start { get; private set; }
        /// <summary>
        /// Get the <see cref="FileProperty"/> to select the video file to reference.
        /// </summary>
        [DataMember]
        public FileProperty File { get; private set; }

        /// <inheritdoc/>
        protected override Image<BGRA32>? OnRender(EffectRenderArgs args)
        {
            float speed = Speed[args.Frame] / 100;
            int start = (int)Start[args.Frame];
            Image<BGRA32>? image = null;
            var time = new Frame((int)((start + args.Frame - Parent!.Start) * speed)).ToTimeSpan(Parent.Parent.Parent!.Framerate);

            _videoReader?.Read(time, out image);

            return image;
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            base.OnLoad();
            Speed.Load(SpeedMetadata);
            Start.Load(StartMetadata);
            File.Load(FileMetadata);

            if (System.IO.File.Exists(File.Value))
            {
                _videoReader = VideoDecoderFactory.Default.Create(File.Value);
            }

            _disposable = File.Subscribe(filename =>
            {
                _videoReader?.Dispose();

                try
                {
                    _videoReader = VideoDecoderFactory.Default.Create(filename);
                }
                catch (Exception)
                {
                    var mes = ServiceProvider?.GetService<IMessage>();
                    mes?.Snackbar(string.Format(Strings.FailedToLoad, filename));
                }
            });
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            base.OnUnload();
            Speed.Unload();
            Start.Unload();
            File.Unload();

            _videoReader?.Dispose();
            _disposable?.Dispose();
        }
    }
}