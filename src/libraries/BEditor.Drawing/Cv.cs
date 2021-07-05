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
        [Obsolete("Use GaussianBlur(Image<BGRA32>, Size, double, double)")]
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
        /// Blurs an image using a Gaussian filter.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="kernelSize">The smoothing kernel size.</param>
        /// <param name="sigmaX">Gaussian kernel standard deviation in X direction.</param>
        /// <param name="sigmaY">Gaussian kernel standard deviation in Y direction.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void GaussianBlur(Image<BGRA32> image, Size kernelSize, double sigmaX, double sigmaY)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            using var mat = image.ToMat();
            var width = kernelSize.Width;
            var height = kernelSize.Height;

            if (width % 2 == 0)
            {
                width++;
            }

            if (height % 2 == 0)
            {
                height++;
            }

            Cv2.GaussianBlur(mat, mat, new(width, height), sigmaX, sigmaY);
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
        [Obsolete("Use Blur(Image<BGRA32>, Size)")]
        public static void Blur(Image<BGRA32> image, int kernelSize)
        {
            using var mat = image.ToMat();

            if (kernelSize % 2 == 0)
            {
                kernelSize++;
            }

            Cv2.Blur(mat, mat, new(kernelSize, kernelSize));
        }

        /// <summary>
        /// Smoothes image using normalized box filter.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="kernelSize">The smoothing kernel size.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Blur(Image<BGRA32> image, Size kernelSize)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            using var mat = image.ToMat();

            var width = kernelSize.Width;
            var height = kernelSize.Height;

            if (width % 2 == 0)
            {
                width++;
            }

            if (height % 2 == 0)
            {
                height++;
            }

            Cv2.Blur(mat, mat, new(width, height));
        }

        internal static unsafe Mat ToMat(this Image<BGRA32> image)
        {
            fixed (BGRA32* ptr = image.Data)
            {
                return new Mat(image.Height, image.Width, MatType.CV_8UC4, (IntPtr)ptr);
            }
        }

        internal static unsafe Mat ToMat(this Image<Grayscale8> image)
        {
            fixed (Grayscale8* ptr = image.Data)
            {
                return new Mat(image.Height, image.Width, MatType.CV_8UC1, (IntPtr)ptr);
            }
        }
    }
}