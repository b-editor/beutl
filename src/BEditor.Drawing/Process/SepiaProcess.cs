
using BEditor.Drawing.Pixel;

using static BEditor.Drawing.Image;

namespace BEditor.Drawing.Process
{
    internal readonly unsafe struct SepiaProcess
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;

        public SepiaProcess(BGRA32* src, BGRA32* dst)
        {
            _src = src;
            _dst = dst;
        }

        public void Invoke(int pos)
        {
            var ntsc = Set255Round(
                (_src[pos].B * 0.289) +
                (_src[pos].G * 0.586) +
                (_src[pos].R * 0.114));

            _dst[pos].B = (byte)Set255(ntsc + 30);
            _dst[pos].G = (byte)ntsc;
            _dst[pos].R = (byte)Set255(ntsc - 20);
        }
    }
}
