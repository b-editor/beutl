using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using FFMediaToolkit;
using FFMediaToolkit.Decoding;

using FFmpeg.AutoGen;

namespace BEditor.Media.Decoder
{
    public unsafe class FFmpegDecoder : IVideoDecoder
    {
        private readonly MediaFile media;
        static FFmpegDecoder()
        {
            FFmpegLoader.FFmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
            FFmpegLoader.LoadFFmpeg();
        }
        public FFmpegDecoder(string filename)
        {
            media = MediaFile.Open(filename);
        }

        public int Fps => media.Video.Info.RealFrameRate.num;
        public Frame FrameCount => media.Video.Info.NumberOfFrames ?? 0;
        public int Width => media.Video.Info.FrameSize.Width;
        public int Height => media.Video.Info.FrameSize.Height;
        public bool IsDisposed { get; private set; }


        public void Dispose()
        {
            if (IsDisposed) return;

            media.Dispose();

            IsDisposed = true;
        }
        public void Read(Frame frame, out Image<BGRA32> image)
        {
            var img = media.Video.ReadFrame(frame);
            using var rgb = new Image<BGR24>(Width, Height);

            fixed (void* dst = rgb.Data)
            fixed (void* src = img.Data)
            {
                Buffer.MemoryCopy(src, dst, rgb.DataSize, rgb.DataSize);
            }

            image = rgb.Convert<BGR24, BGRA32>();
        }
    }
}
