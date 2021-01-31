
using System;

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.Process
{
    internal unsafe readonly struct ColorKeyProcess
    {
        private readonly BGRA32* dst;
        private readonly BGRA32* src;
        private readonly BGRA32 color;
        private readonly int value;

        public ColorKeyProcess(BGRA32* src, BGRA32* dst, BGRA32 color, int value)
        {
            this.dst = dst;
            this.src = src;
            this.color = color;
            this.value = value;
        }

        public readonly void Invoke(int pos)
        {
            var camColor = src[pos];

            if (Math.Abs(color.R - camColor.R) < value
                && Math.Abs(color.G - camColor.G) < value
                && Math.Abs(color.B - camColor.B) < value)
            {
                camColor = default;
            }
            //if (value.R == camColor.R
            //    && value.G == camColor.G
            //    && value.B == camColor.B)
            //{
            //    camColor = default;
            //}

            dst[pos] = camColor;
        }
    }
}
