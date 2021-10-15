using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Media;
using BEditor.Media.Decoding;

using FFMediaToolkit.Graphics;

namespace BEditor.Extensions.FFmpeg.Decoder
{
    public sealed class VideoStream : IVideoStream
    {
        private readonly FFMediaToolkit.Decoding.VideoStream _stream;

        public VideoStream(FFMediaToolkit.Decoding.VideoStream stream)
        {
            _stream = stream;
            Info = new(
                stream.Info.CodecName,
                MediaType.Video,
                stream.Info.Duration - stream.Info.StartTime ?? default,
                new(stream.Info.FrameSize.Width, stream.Info.FrameSize.Height),
                (int)stream.Info.NumberOfFrames!,
                new Rational(stream.Info.RealFrameRate.num, stream.Info.RealFrameRate.den));
        }

        public VideoStreamInfo Info { get; }

        public void Dispose()
        {
            _stream.Dispose();
        }

        public Image<BGRA32> GetFrame(TimeSpan time)
        {
            lock (this)
            {
                return ToImage(_stream.GetFrame(time));
            }
        }

        public Image<BGRA32> GetNextFrame()
        {
            lock (this)
            {
                return ToImage(_stream.GetNextFrame());
            }
        }

        public bool TryGetFrame(TimeSpan time, [NotNullWhen(true)] out Image<BGRA32>? image)
        {
            lock (this)
            {
                var result = _stream.TryGetFrame(time, out var data);
                if (result)
                {
                    image = ToImage(data);
                }
                else
                {
                    image = null;
                }

                return result;
            }
        }

        public bool TryGetNextFrame([NotNullWhen(true)] out Image<BGRA32>? image)
        {
            lock (this)
            {
                var result = _stream.TryGetNextFrame(out var data);
                if (result)
                {
                    image = ToImage(data);
                }
                else
                {
                    image = null;
                }

                return result;
            }
        }

        private static unsafe Image<BGRA32> ToImage(ImageData data)
        {
            var img = new Image<BGRA32>(data.ImageSize.Width, data.ImageSize.Height);

            fixed (byte* src = data.Data)
            fixed (BGRA32* dst = img.Data)
            {
                Buffer.MemoryCopy(src, dst, img.DataSize, img.DataSize);
            }

            return img;
        }
    }
}