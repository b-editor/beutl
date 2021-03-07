using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
            _ = FFmpegContext.Current;
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
        public void Read(TimeSpan time, out Sound<PCM32> left, out Sound<PCM32> right)
        {
            var audio = media.Audio.GetFrame(time);
            var array = audio.GetSampleData();

            left = new((uint)media.Audio.Info.SampleRate, (uint)audio.NumSamples);
            right = new((uint)media.Audio.Info.SampleRate, (uint)audio.NumSamples);

            array[0].Select(i => Unsafe.As<float, PCM32>(ref i)).ToArray().CopyTo(left.Pcm.AsSpan());

            if (array.Length is 2)
            {
                array[1].Select(i => Unsafe.As<float, PCM32>(ref i)).ToArray().CopyTo(right.Pcm.AsSpan());
            }
        }
        public void ReadAll(out Sound<PCM32> left, out Sound<PCM32> right)
        {
            media.Audio.TryGetFrame(TimeSpan.Zero, out _);
            var sampleL = new List<PCM32>();
            var sampleR = new List<PCM32>();

            while (media.Audio.TryGetNextFrame(out var audio))
            {
                var array = audio.GetSampleData();

                sampleL.AddRange(array[0].Select(i => Unsafe.As<float, PCM32>(ref i)));

                if (array.Length is 2)
                {
                    sampleR.AddRange(array[1].Select(i => Unsafe.As<float, PCM32>(ref i)));
                }
            }

            left = new((uint)media.Audio.Info.SampleRate, (uint)sampleL.Count);
            right = new((uint)media.Audio.Info.SampleRate, (uint)sampleR.Count);

            sampleL.CopyTo(left.Pcm);
            sampleR.CopyTo(right.Pcm);
        }
        public void ReadAll(out Sound<PCM16> left, out Sound<PCM16> right)
        {
            ReadAll(out Sound<PCM32> left32, out Sound<PCM32> right32);

            left = left32.Convert<PCM16>();
            right = right32.Convert<PCM16>();
        }
        public void Read(TimeSpan time, out Sound<PCM16> left, out Sound<PCM16> right)
        {
            Read(time, out Sound<PCM32> left32, out Sound<PCM32> right32);

            left = left32.Convert<PCM16>();
            right = right32.Convert<PCM16>();
        }
    }
}
