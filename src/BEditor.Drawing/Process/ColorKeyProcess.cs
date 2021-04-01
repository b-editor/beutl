
using System;

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.Process
{
    internal unsafe readonly struct ColorKeyProcess
    {
        private readonly BGRA32* _dst;
        private readonly BGRA32* _src;
        private readonly BGRA32 _color;
        private readonly int _value;

        public ColorKeyProcess(BGRA32* src, BGRA32* dst, BGRA32 color, int value)
        {
            _dst = dst;
            _src = src;
            _color = color;
            _value = value;
        }

        public readonly void Invoke(int pos)
        {
            var camColor = _src[pos];

            if (Math.Abs(_color.R - camColor.R) < _value
                && Math.Abs(_color.G - camColor.G) < _value
                && Math.Abs(_color.B - camColor.B) < _value)
            {
                camColor = default;
            }

            _dst[pos] = camColor;
        }
    }
}
