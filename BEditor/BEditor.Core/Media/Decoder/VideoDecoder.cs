using System;
using System.Collections.Generic;
using System.Text;

using BEditor.Drawing;

namespace BEditor.Core.Media.Decoder
{
    public abstract class VideoDecoder : IDisposable
    {
        public VideoDecoder(string fileName)
        {
            FileName = fileName;
        }

        public string FileName { get; }
        public abstract int Fps { get; }
        public abstract int FrameCount { get; }
        public abstract int Width { get; }
        public abstract int Height { get; }

        public abstract Image<BGRA32> Read(int frame);

        public abstract void Dispose();
    }
}
