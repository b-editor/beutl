using System;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Media.Encoder
{
    public interface IVideoEncoder : IDisposable
    {
        public int Fps { get; }
        public int Width { get; }
        public int Height { get; }

        public void Write(Image<BGRA32> image);
    }
}
