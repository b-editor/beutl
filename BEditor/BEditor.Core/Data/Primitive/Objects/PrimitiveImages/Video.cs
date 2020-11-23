using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Extensions.ViewCommand;
using BEditor.Core.Media.Decoder;
using BEditor.Core.Properties;

namespace BEditor.Core.Data.Primitive.Objects.PrimitiveImages
{
    [DataContract(Namespace = "")]
    public class Video : ImageObject
    {
        public static readonly EasePropertyMetadata SpeedMetadata = new(Resources.Speed, 100);
        public static readonly EasePropertyMetadata StartMetadata = new(Resources.Start, 1, float.NaN, 0);
        public static readonly FilePropertyMetadata FileMetadata = new(Resources.File, "", "mp4,avi,wmv,mov", Resources.VideoFile);
        private VideoDecoder videoReader;


        public Video()
        {
            Speed = new(SpeedMetadata);
            Start = new(StartMetadata);
            File = new(FileMetadata);
        }


        #region Properties

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

        #endregion


        public override Media.Image OnRender(EffectRenderArgs args)
        {
            float speed = Speed.GetValue(args.Frame) / 100;
            int start = (int)Start.GetValue(args.Frame);

            return VideoDecoder.Read((int)((start + args.Frame - Parent.Start) * speed), videoReader);
        }

        public override void PropertyLoaded()
        {
            base.PropertyLoaded();
            Speed.ExecuteLoaded(SpeedMetadata);
            Start.ExecuteLoaded(StartMetadata);
            File.ExecuteLoaded(FileMetadata);

            if (System.IO.File.Exists(File.File))
            {
                videoReader = new FFmpegVideoDecoder(File.File);
            }

            File.Subscribe(filename =>
            {
                videoReader?.Dispose();

                try
                {
                    videoReader = new FFmpegVideoDecoder(filename);
                }
                catch (Exception ex)
                {
                    Message.Snackbar(string.Format(Resources.FailedToLoad, filename));
                    ActivityLog.ErrorLog(ex);
                }
            });
        }
    }
}
