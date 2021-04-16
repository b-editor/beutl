using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using FFMediaToolkit;
using FFMediaToolkit.Encoding;
using FFMediaToolkit.Graphics;

namespace BEditor.Media.Encoder
{
    public class FFmpegEncoder : IVideoEncoder
    {
        private readonly string file;
        private readonly MediaOutput media;
        private readonly MediaBuilder builder;

        public FFmpegEncoder(int width, int height, int fps, VideoCodec codec, string filename)
        {
            filename = Path.GetFullPath(filename);

            file = filename;
            builder = MediaBuilder.CreateContainer(filename)
                .WithVideo(new(width, height, fps, (FFMediaToolkit.Encoding.VideoCodec)codec));
            media = builder.Create();

            var config = media.Video.Configuration;
            config.VideoFormat = ImagePixelFormat.Bgra32;
        }

        public int Fps => media.Video.Configuration.Framerate;
        public int Width => media.Video.Configuration.VideoWidth;
        public int Height => media.Video.Configuration.VideoHeight;

        public void Dispose()
        {
            media.Dispose();

            GC.SuppressFinalize(this);
        }
        public unsafe void Write(Image<BGRA32> image)
        {
            fixed (void* data = image.Data)
            {
                var img = ImageData.FromPointer(new IntPtr(data), ImagePixelFormat.Bgra32, new(Width, Height));

                media.Video.AddFrame(img);
            }
        }
    }
}