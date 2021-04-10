
using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.PixelOperation
{
    public readonly unsafe struct BinarizationOperation : IPixelOperation
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;
        private readonly byte _value;

        public BinarizationOperation(BGRA32* src, BGRA32* dst, byte value)
        {
            _src = src;
            _dst = dst;
            _value = value;
        }

        public readonly void Invoke(int pos)
        {
            if (_src[pos].R <= _value &&
                _src[pos].G <= _value &&
                _src[pos].B <= _value)
            {
                _dst[pos].R = _dst[pos].G = _dst[pos].B = 0;
            }
            else
            {
                _dst[pos].R = _dst[pos].G = _dst[pos].B = 255;
            }
            _dst[pos].A = _src[pos].A;
        }
    }
}
