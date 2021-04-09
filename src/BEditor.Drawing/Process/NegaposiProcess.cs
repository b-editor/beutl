
using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.Process
{
    public readonly unsafe struct NegaposiProcess : IPixelProcess
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;
        private readonly byte _red;
        private readonly byte _green;
        private readonly byte _blue;

        public NegaposiProcess(BGRA32* src, BGRA32* dst, byte red, byte green, byte blue)
        {
            _src = src;
            _dst = dst;
            _red = red;
            _green = green;
            _blue = blue;
        }

        public readonly void Invoke(int pos)
        {
            _dst[pos].B = (byte)(_red - _src[pos].B);
            _dst[pos].G = (byte)(_green - _src[pos].G);
            _dst[pos].R = (byte)(_blue - _src[pos].R);
            _dst[pos].A = _src[pos].A;
        }
    }
}
