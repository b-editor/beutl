using System;
using System.Collections.Generic;
using System.Linq;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Media.Decoding;
using BEditor.Media.Graphics;
using BEditor.Media.PCM;

namespace BEditor.Media
{
    public unsafe class FFmpegDecoder
    {
        private readonly MediaFile _media;

        public FFmpegDecoder(string filename)
        {
            _media = MediaFile.Open(filename, new MediaOptions
            {
                VideoPixelFormat = ImagePixelFormat.Bgra32,
            });
        }
        ~FFmpegDecoder()
        {
            Dispose();
        }

        public int Fps => _media.Video!.Info.RealFrameRate.num;
        public Frame FrameCount => _media.Video!.Info.NumberOfFrames ?? 0;
        public int Width => _media.Video!.Info.FrameSize.Width;
        public int Height => _media.Video!.Info.FrameSize.Height;
        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (IsDisposed) return;

            _media?.Dispose();
            GC.SuppressFinalize(this);

            IsDisposed = true;
        }

        public void Read(Frame frame, out Image<BGRA32> image)
        {
            var img = _media.Video!.GetFrame(frame.ToTimeSpan(Fps));
            image = new Image<BGRA32>(Width, Height);

            fixed (void* dst = image.Data)
            fixed (void* src = img.Data)
            {
                Buffer.MemoryCopy(src, dst, image.DataSize, image.DataSize);
            }
        }

        public void Read(TimeSpan time, out Image<BGRA32> image)
        {
            var img = _media.Video!.GetFrame(time);
            image = new Image<BGRA32>(Width, Height);

            fixed (void* dst = image.Data)
            fixed (void* src = img.Data)
            {
                Buffer.MemoryCopy(src, dst, image.DataSize, image.DataSize);
            }
        }

        public void Read(TimeSpan time, out Sound<StereoPCMFloat> sound)
        {
            var data = _media.Audio!.GetFrame(time);
            sound = new(_media.Audio.Info.SampleRate, data.NumSamples);

            var left = data.GetChannelData(0);
            var right = data.GetChannelData(1);

            for (var i = 0; i < sound.Data.Length; i++)
            {
                sound.Data[i] = new StereoPCMFloat(left[i], right[i]);
            }
        }

        public void ReadAll(out Sound<StereoPCM32> sound)
        {
            _media.Audio!.TryGetFrame(TimeSpan.Zero, out _);
            var sampleL = new List<int>();
            var sampleR = new List<int>();

            while (_media.Audio.TryGetNextFrame(out var audio))
            {
                var array = audio.GetSampleData();

                sampleL.AddRange(array[0].Select(i => (int)(i * int.MaxValue)));

                if (array.Length is 2)
                {
                    sampleR.AddRange(array[1].Select(i => (int)(i * int.MaxValue)));
                }
            }

            sound = new(_media.Audio.Info.SampleRate, sampleL.Count);

            sampleL.Zip(sampleR, (l, r) => new StereoPCM32(l, r))
                .ToArray()
                .CopyTo(sound.Data);
        }

        public void ReadAll(out Sound<StereoPCM16> sound)
        {
            _media.Audio!.TryGetFrame(TimeSpan.Zero, out _);
            var sampleL = new List<short>();
            var sampleR = new List<short>();

            while (_media.Audio.TryGetNextFrame(out var audio))
            {
                var array = audio.GetSampleData();

                sampleL.AddRange(array[0].Select(i => (short)(i * short.MaxValue)));

                if (array.Length is 2)
                {
                    sampleR.AddRange(array[1].Select(i => (short)(i * short.MaxValue)));
                }
            }

            sound = new(_media.Audio.Info.SampleRate, sampleL.Count);

            sampleL.Zip(sampleR, (l, r) => new StereoPCM16(l, r))
                .ToArray()
                .CopyTo(sound.Data);
        }
    }
}