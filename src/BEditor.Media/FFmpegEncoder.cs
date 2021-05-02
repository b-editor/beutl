using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Media.PCM;

using BEditor.Media;
using BEditor.Media.Encoding;
using BEditor.Media.Graphics;

namespace BEditor.Media
{
    public class FFmpegEncoder
    {
        private readonly string _file;
        private readonly MediaOutput _media;
        private readonly MediaBuilder _builder;

        public FFmpegEncoder(int width, int height, int fps, int samplerate, VideoCodec videocodec, AudioCodec audiocodec, string filename)
        {
            filename = Path.GetFullPath(filename);

            _file = filename;
            _builder = MediaBuilder.CreateContainer(filename)
                .WithVideo(new(width, height, fps, videocodec))
                .WithAudio(new(samplerate, 2, audiocodec));

            _media = _builder.Create();

            var config = _media.Video!.Configuration;
            config.VideoFormat = ImagePixelFormat.Bgra32;
        }

        public int Fps => _media.Video!.Configuration.Framerate;
        public int Width => _media.Video!.Configuration.VideoWidth;
        public int Height => _media.Video!.Configuration.VideoHeight;

        public void Dispose()
        {
            _media.Dispose();

            GC.SuppressFinalize(this);
        }
        public unsafe void Write(Image<BGRA32> image)
        {
            fixed (void* data = image.Data)
            {
                var img = ImageData.FromPointer(new IntPtr(data), ImagePixelFormat.Bgra32, new(Width, Height));

                _media.Video!.AddFrame(img);
            }
        }
        public unsafe void Write(Sound<StereoPCMFloat> sound)
        {
            var left = new float[sound.Data.Length];
            var right = new float[sound.Data.Length];

            for (var i = 0; i < sound.Data.Length; i++)
            {
                left[i] = sound.Data[i].Left;
                right[i] = sound.Data[i].Right;
            }

            _media.Audio!.AddFrame(new float[][] { left, right });
        }
    }
}