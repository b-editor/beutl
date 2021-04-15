using System;
using System.Collections.Generic;
using System.Text;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Media.PCM;

namespace BEditor.Media.Decoder
{
    public interface IMediaDecoder : IDisposable
    {
        public int Fps { get; }
        public Frame FrameCount { get; }
        public int Width { get; }
        public int Height { get; }

        public void Read(Frame frame, out Image<BGRA32> image);
        public void Read(TimeSpan time, out Image<BGRA32> image);
        public void Read(TimeSpan time, out Sound<PCM32> left, out Sound<PCM32> right);
    }
}