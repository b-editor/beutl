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
    [DataContract]
    public class VideoFile : ImageObject
    {
        public static readonly EasePropertyMetadata SpeedMetadata = new(Resources.Speed, 100);
        public static readonly EasePropertyMetadata StartMetadata = new(Resources.Start, 1, float.NaN, 0);
        public static readonly FilePropertyMetadata FileMetadata = new(Resources.File, "", new(Resources.VideoFile, new FileExtension[]
        {
            new("mp4"),
            new("avi"),
            new("wmv"),
            new("mov")
        }));
        private IVideoDecoder? _VideoReader;
        private IDisposable? _Disposable;

        public VideoFile()
        {
            Speed = new(SpeedMetadata);
            Start = new(StartMetadata);
            File = new(FileMetadata);
        }

        public override string Name => Resources.Video;
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
        [DataMember(Order = 0)]
        public EaseProperty Speed { get; private set; }
        [DataMember(Order = 1)]
        public EaseProperty Start { get; private set; }
        [DataMember(Order = 2)]
        public FileProperty File { get; private set; }

        protected override Image<BGRA32>? OnRender(EffectRenderArgs args)
        {
            float speed = Speed[args.Frame] / 100;
            int start = (int)Start[args.Frame];
            Image<BGRA32>? image = null;

            _VideoReader?.Read((int)((start + args.Frame - Parent!.Start) * speed), out image);

            return image;
        }
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
                catch (Exception ex)
                {
                    Message.Snackbar(string.Format(Resources.FailedToLoad, filename));
                }
            });
        }
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
