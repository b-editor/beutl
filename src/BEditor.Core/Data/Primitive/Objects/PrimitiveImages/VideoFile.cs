using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;
using BEditor.Core.Service;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Media.Decoder;

namespace BEditor.Core.Data.Primitive.Objects
{
    /// <summary>
    /// Represents an <see cref="ImageObject"/> that references an video file.
    /// </summary>
    [DataContract]
    public class VideoFile : ImageObject
    {
        /// <summary>
        /// Represents <see cref="Speed"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata SpeedMetadata = new(Resources.Speed, 100);
        /// <summary>
        /// Represents <see cref="Start"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata StartMetadata = new(Resources.Start, 1, float.NaN, 0);
        /// <summary>
        /// Represents <see cref="File"/> metadata.
        /// </summary>
        public static readonly FilePropertyMetadata FileMetadata = new(Resources.File, "", new(Resources.VideoFile, new FileExtension[]
        {
            new("mp4"),
            new("avi"),
            new("wmv"),
            new("mov")
        }));
        private IVideoDecoder? _VideoReader;
        private IDisposable? _Disposable;

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
        public override string Name => Resources.Video;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Coordinate,
            Zoom,
            Blend,
            Angle,
            Material,
            Speed,
            Start,
            File
        };
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the playback speed.
        /// </summary>
        [DataMember(Order = 0)]
        public EaseProperty Speed { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the start position.
        /// </summary>
        [DataMember(Order = 1)]
        public EaseProperty Start { get; private set; }
        /// <summary>
        /// Get the <see cref="FileProperty"/> to select the video file to reference.
        /// </summary>
        [DataMember(Order = 2)]
        public FileProperty File { get; private set; }

        /// <inheritdoc/>
        protected override Image<BGRA32>? OnRender(EffectRenderArgs args)
        {
            float speed = Speed[args.Frame] / 100;
            int start = (int)Start[args.Frame];
            Image<BGRA32>? image = null;

            _VideoReader?.Read((int)((start + args.Frame - Parent!.Start) * speed), out image);

            return image;
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            base.OnLoad();
            Speed.Load(SpeedMetadata);
            Start.Load(StartMetadata);
            File.Load(FileMetadata);

            if (System.IO.File.Exists(File.File))
            {
                _VideoReader = VideoDecoderFactory.Default.Create(File.File);
            }

            _Disposable = File.Subscribe(filename =>
            {
                _VideoReader?.Dispose();

                try
                {
                    _VideoReader = VideoDecoderFactory.Default.Create(filename);
                }
                catch (Exception)
                {
                    Message.Snackbar(string.Format(Resources.FailedToLoad, filename));
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

            _VideoReader?.Dispose();
            _Disposable?.Dispose();
        }
    }
}
