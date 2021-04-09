using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.Pixel;
using BEditor.Drawing.Process;

using SkiaSharp;

namespace BEditor.Drawing
{
    public static unsafe partial class Image
    {
        internal static double Set255(double value)
        {
            if (value > 255) return 255;
            else if (value < 0) return 0;

            return value;
        }

        internal static float Set255(float value)
        {
            if (value > 255) return 255;
            else if (value < 0) return 0;

            return value;
        }

        internal static double Set255Round(double value)
        {
            if (value > 255) return 255;
            else if (value < 0) return 0;

            return Math.Round(value);
        }

        internal static float Set255Round(float value)
        {
            if (value > 255) return 255;
            else if (value < 0) return 0;

            return MathF.Round(value);
        }

        internal static int Random(int max, int min)
        {
            var random = new Random();

            return (int)MathF.Floor((random.Next() * ((max + 1) - min)) + min);
        }

        public static void Grayscale(this Image<BGRA32> image)
        {
            var height = image.Height;
            var width = image.Width;
            fixed (BGRA32* raw = image.Data)
            {
                Parallel.For(0, image.Data.Length, new GrayscaleProcess(raw, raw).Invoke);
            }
        }
        
        public static void Sepia(this Image<BGRA32> image)
        {
            var height = image.Height;
            var width = image.Width;
            fixed (BGRA32* raw = image.Data)
            {
                Parallel.For(0, image.Data.Length, new SepiaProcess(raw, raw).Invoke);
            }
        }
    }
}
