using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Media.PCM;

using FFMediaToolkit;
using FFMediaToolkit.Decoding;
using FFMediaToolkit.Graphics;

namespace BEditor.Media.Decoder
{
    public unsafe class FFmpegDecoder : IMediaDecoder

    {
        private readonly MediaFile media;
        static FFmpegDecoder()
        {
            FFmpegLoader.FFmpegPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg");
            FFmpegLoader.LoadFFmpeg();
        }
        public FFmpegDecoder(string filename)
        {
            media = MediaFile.Open(filename, new MediaOptions()
            {
                VideoPixelFormat = ImagePixelFormat.Bgra32,
            });
        }
        ~FFmpegDecoder()
        {
            Dispose();
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
            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
        public void Read(Frame frame, out Image<BGRA32> image)
        {
            var img = media.Video.GetFrame(frame.ToTimeSpan(Fps));
            image = new Image<BGRA32>(Width, Height);

            fixed (void* dst = image.Data)
            fixed (void* src = img.Data)
            {
                Buffer.MemoryCopy(src, dst, image.DataSize, image.DataSize);
            }
        }
        public void Read(TimeSpan time, out Image<BGRA32> image)
        {
            var img = media.Video.GetFrame(time);
            image = new Image<BGRA32>(Width, Height);

            fixed (void* dst = image.Data)
            fixed (void* src = img.Data)
            {
                Buffer.MemoryCopy(src, dst, image.DataSize, image.DataSize);
            }
        }
        public void Read(TimeSpan time, out Sound<PCM32> sound)
        {
            var audio = media.Audio.GetFrame(time);
            var array = audio.GetSampleData();

            sound = new((Channel)media.Audio.Info.NumChannels, (uint)media.Audio.Info.SampleRate, (uint)audio.NumSamples);

            for (int i = 0; i < sound.Length; i += 2)
            {
                sound.Pcm[i] = array[0][i / 2];
            }
            for (int i = 1; i < sound.Length; i += 2)
            {
                sound.Pcm[i] = array[1][i / 2];
            }
        }
    }
}
