
using System;

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.PixelOperation
{
    public unsafe readonly struct ChromaKeyOperation : IPixelOperation
    {
        private readonly BGRA32* _dst;
        private readonly BGRA32* _src;
        private readonly int _value;

        public ChromaKeyOperation(BGRA32* src, BGRA32* dst, int value)
        {
            _dst = dst;
            _src = src;
            _value = value;
        }

        public readonly void Invoke(int pos)
        {
            var camColor = _src[pos];

            var max = Math.Max(Math.Max(camColor.R, camColor.G), camColor.B);
            var min = Math.Min(Math.Min(camColor.R, camColor.G), camColor.B);

            var replace =
                camColor.G != min
                && (camColor.G == max
                || max - camColor.G < 8)
                && (max - min) > _value;

            if (replace)
            {
                camColor = default;
            }

            _dst[pos] = camColor;
        }
    }
}