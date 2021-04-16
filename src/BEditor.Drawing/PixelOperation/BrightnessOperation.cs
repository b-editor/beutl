
using BEditor.Drawing.Pixel;

using static BEditor.Drawing.Image;

namespace BEditor.Drawing.PixelOperation
{
    public readonly unsafe struct BrightnessOperation : IPixelOperation
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;
        // -255 - 255
        private readonly short _light;

        public BrightnessOperation(BGRA32* src, BGRA32* dst, short light)
        {
            _src = src;
            _dst = dst;
            _light = light;
        }

        public readonly void Invoke(int pos)
        {
            _dst[pos].B = (byte)Set255(_src[pos].B + _light);
            _dst[pos].G = (byte)Set255(_src[pos].G + _light);
            _dst[pos].R = (byte)Set255(_src[pos].R + _light);
            _dst[pos].A = _src[pos].A;
        }
    }
}