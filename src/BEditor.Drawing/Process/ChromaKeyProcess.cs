
using System;

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.Process
{
    internal unsafe readonly struct ChromaKeyProcess
    {
        private readonly BGRA32* _dst;
        private readonly BGRA32* _src;
        private readonly int _value;

        public ChromaKeyProcess(BGRA32* src, BGRA32* dst, int value)
        {
            _dst = dst;
            _src = src;
            _value = value;
        }

        public readonly void Invoke(int pos)
        {
            var camColor = _src[pos];

            byte max = Math.Max(Math.Max(camColor.R, camColor.G), camColor.B);
            byte min = Math.Min(Math.Min(camColor.R, camColor.G), camColor.B);

            bool replace =
                camColor.G != min // green is not the smallest value
                && (camColor.G == max // green is the biggest value
                || max - camColor.G < 8) // or at least almost the biggest value
                && (max - min) > _value; // minimum difference between smallest/biggest value (avoid grays)

            //bool replace = color <= camColor && camColor <= color;//min < camColor && camColor < max;


            if (replace)
                camColor = default;

            _dst[pos] = camColor;
        }
    }
}
