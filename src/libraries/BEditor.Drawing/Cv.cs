// Cv.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Drawing.Pixel;

using OpenCvSharp;

namespace BEditor.Drawing
{
    /// <summary>
    /// Provides image processing using OpenCV.
    /// </summary>
    public static class Cv
    {
        /// <summary>
        /// Blurs an image using a Gaussian filter.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="kernelSize">The smoothing kernel size.</param>
        public static void GaussianBlur(Image<BGRA32> image, int kernelSize)
        {
            using var mat = image.ToMat();

            if (kernelSize % 2 == 0)
            {
                kernelSize++;
            }

            Cv2.GaussianBlur(mat, mat, new(kernelSize, kernelSize), 0, 0);
        }

        /// <summary>
        /// Smoothes image using median filter.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="kernelSize">The smoothing kernel size.</param>
        public static void MedianBlur(Image<BGRA32> image, int kernelSize)
        {
            using var mat = image.ToMat();

            if (kernelSize % 2 == 0)
            {
                kernelSize++;
            }

            Cv2.MedianBlur(mat, mat, kernelSize);
        }

        /// <summary>
        /// Smoothes image using normalized box filter.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="kernelSize">The smoothing kernel size.</param>
        public static void Blur(Image<BGRA32> image, int kernelSize)
        {
            using var mat = image.ToMat();

            if (kernelSize % 2 == 0)
            {
                kernelSize++;
            }

            Cv2.Blur(mat, mat, new(kernelSize, kernelSize));
        }

        private static unsafe Mat ToMat(this Image<BGRA32> image)
        {
            fixed (BGRA32* ptr = image.Data)
            {
                return new Mat(image.Height, image.Width, MatType.CV_8UC4, (IntPtr)ptr);
            }
        }
    }
}