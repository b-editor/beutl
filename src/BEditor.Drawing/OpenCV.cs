using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.Pixel;

using OpenCvSharp;

namespace BEditor.Drawing
{
    public static class Cv
    {
        private unsafe static Mat ToMat(this Image<BGRA32> image)
        {
            fixed (BGRA32* ptr = image.Data)
            {
                return new Mat(image.Height, image.Width, MatType.CV_8UC4, (IntPtr)ptr);
            }
        }

        public static void GaussianBlur(Image<BGRA32> image, int kernelSize)
        {
            using var mat = image.ToMat();

            if (kernelSize % 2 == 0)
            {
                kernelSize++;
            }

            Cv2.GaussianBlur(mat, mat, new(kernelSize, kernelSize), 0, 0);
        }

        public static void MedianBlur(Image<BGRA32> image, int kernelSize)
        {
            using var mat = image.ToMat();

            if (kernelSize % 2 == 0)
            {
                kernelSize++;
            }

            Cv2.MedianBlur(mat, mat, kernelSize);
        }

        public static void Blur(Image<BGRA32> image, int kernelSize)
        {
            using var mat = image.ToMat();

            if (kernelSize % 2 == 0)
            {
                kernelSize++;
            }

            Cv2.Blur(mat, mat, new(kernelSize, kernelSize));
        }
    }
}
