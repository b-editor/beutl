
using System;

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.Process
{
    internal unsafe readonly struct ChromaKeyProcess
    {
        private readonly BGRA32* dst;
        private readonly BGRA32* src;
        private readonly int value;

        public ChromaKeyProcess(BGRA32* src, BGRA32* dst, int value)
        {
            this.dst = dst;
            this.src = src;
            this.value = value;
        }

        public readonly void Invoke(int pos)
        {
            var camColor = src[pos];

            byte max = Math.Max(Math.Max(camColor.R, camColor.G), camColor.B);
            byte min = Math.Min(Math.Min(camColor.R, camColor.G), camColor.B);

            bool replace =
                camColor.G != min // green is not the smallest value
                && (camColor.G == max // green is the biggest value
                || max - camColor.G < 8) // or at least almost the biggest value
                && (max - min) > value; // minimum difference between smallest/biggest value (avoid grays)

            //bool replace = color <= camColor && camColor <= color;//min < camColor && camColor < max;


            if (replace)
                camColor = default;

            dst[pos] = camColor;
        }
    }
}
