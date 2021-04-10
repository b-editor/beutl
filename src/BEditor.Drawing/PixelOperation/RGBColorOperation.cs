
using BEditor.Drawing.Pixel;

using static BEditor.Drawing.Image;

namespace BEditor.Drawing.PixelOperation
{
    public readonly unsafe struct RGBColorOperation : IPixelOperation
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;
        private readonly short _r;
        private readonly short _g;
        private readonly short _b;

        public RGBColorOperation(BGRA32* src, BGRA32* dst, short r,short g,short b)
        {
            _src = src;
            _dst = dst;
            (_r, _g, _b) = (r, g, b);
        }

        public readonly void Invoke(int pos)
        {
            _dst[pos].B = (byte)Set255(_src[pos].B + _b);
            _dst[pos].G = (byte)Set255(_src[pos].G + _g);
            _dst[pos].R = (byte)Set255(_src[pos].R + _r);
            _dst[pos].A = _src[pos].A;
        }
    }
}
