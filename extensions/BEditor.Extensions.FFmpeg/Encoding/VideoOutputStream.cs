using System;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Media.Encoding;

using FFMediaToolkit.Graphics;

namespace BEditor.Media.FFmpeg.Encoding
{
    public class VideoOutputStream : IVideoOutputStream
    {
        private readonly FFMediaToolkit.Encoding.VideoOutputStream _stream;

        public VideoOutputStream(FFMediaToolkit.Encoding.VideoOutputStream stream, VideoEncoderSettings config)
        {
            _stream = stream;
            Configuration = config;
        }

        public VideoEncoderSettings Configuration { get; }

        public TimeSpan CurrentDuration => _stream.CurrentDuration;

        public unsafe void AddFrame(Image<BGRA32> image)
        {
            fixed (BGRA32* data = image.Data)
            {
                _stream.AddFrame(ImageData.FromPointer((IntPtr)data, ImagePixelFormat.Bgra32, new(image.Width, image.Height)));
            }
        }

        public void Dispose()
        {
            _stream.Dispose();
        }
    }
}
