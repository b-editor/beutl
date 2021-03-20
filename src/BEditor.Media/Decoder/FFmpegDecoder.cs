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

            left = new(media.Audio.Info.SampleRate, audio.NumSamples);
            right = new(media.Audio.Info.SampleRate, audio.NumSamples);

            array[0].Select(i => (PCM32)(i * int.MaxValue)).ToArray().CopyTo(left.Data);

            if (array.Length is 2)
            {
                array[1].Select(i => (PCM32)(i * int.MaxValue)).ToArray().CopyTo(right.Data);
            }
        }
        public void ReadAll(out Sound<StereoPCM32> sound)
        {
            media.Audio.TryGetFrame(TimeSpan.Zero, out _);
            var sampleL = new List<int>();
            var sampleR = new List<int>();

            while (media.Audio.TryGetNextFrame(out var audio))
            {
                var array = audio.GetSampleData();

                sampleL.AddRange(array[0].Select(i => (int)(i * int.MaxValue)));

                if (array.Length is 2)
                {
                    sampleR.AddRange(array[1].Select(i => (int)(i * int.MaxValue)));
                }
            }

            sound = new(media.Audio.Info.SampleRate, sampleL.Count);

            sampleL.Zip(sampleR, (l, r) => new StereoPCM32(l, r))
                .ToArray()
                .CopyTo(sound.Data);
        }
        public void ReadAll(out Sound<StereoPCM16> sound)
        {
            media.Audio.TryGetFrame(TimeSpan.Zero, out _);
            var sampleL = new List<short>();
            var sampleR = new List<short>();

            while (media.Audio.TryGetNextFrame(out var audio))
            {
                var array = audio.GetSampleData();

                sampleL.AddRange(array[0].Select(i => (short)(i * short.MaxValue)));

                if (array.Length is 2)
                {
                    sampleR.AddRange(array[1].Select(i => (short)(i * short.MaxValue)));
                }
            }

            sound = new(media.Audio.Info.SampleRate, sampleL.Count);

            sampleL.Zip(sampleR, (l, r) => new StereoPCM16(l, r))
                .ToArray()
                .CopyTo(sound.Data);
        }
        public void ReadAll(out Sound<PCM32> left, out Sound<PCM32> right)
        {
            media.Audio.TryGetFrame(TimeSpan.Zero, out _);
            var sampleL = new List<PCM32>();
            var sampleR = new List<PCM32>();

            while (media.Audio.TryGetNextFrame(out var audio))
            {
                var array = audio.GetSampleData();

                sampleL.AddRange(array[0].Select(i => (PCM32)(i * int.MaxValue)));

                if (array.Length is 2)
                {
                    sampleR.AddRange(array[1].Select(i => (PCM32)(i * int.MaxValue)));
                }
            }

            left = new(media.Audio.Info.SampleRate, sampleL.Count);
            right = new(media.Audio.Info.SampleRate, sampleR.Count);

            sampleL.ToArray().AsSpan().CopyTo(left.Data);
            sampleR.ToArray().AsSpan().CopyTo(right.Data);
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
