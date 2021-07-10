// ApplyColorMatrixOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Compute;
using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

namespace BEditor.Drawing
{
    /// <inheritdoc cref="Image"/>
    public static unsafe partial class Image
    {
        /// <summary>
        /// Applies the color matrix.
        /// </summary>
        /// <param name="image">The image to apply the color matrix.</param>
        /// <param name="matrix">The color matrix.</param>
        /// <param name="context">When processing using Gpu, specify a valid DrawingContext.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Apply(this Image<BGRA32> image, ref ColorMatrix matrix, DrawingContext? context = null)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();

            fixed (BGRA32* data = image.Data)
#pragma warning disable RCS1176
            fixed (ColorMatrix* mat = &matrix)
#pragma warning restore RCS1176
            {
                if (context?.IsDisposed == false)
                {
                    image.PixelOperate<ApplyColorMatrixOperation, Float5x5>(context, new Float5x5(
                        matrix.M00, matrix.M01, matrix.M02, matrix.M03, matrix.M04,
                        matrix.M10, matrix.M11, matrix.M12, matrix.M13, matrix.M14,
                        matrix.M20, matrix.M21, matrix.M22, matrix.M23, matrix.M24,
                        matrix.M30, matrix.M31, matrix.M32, matrix.M33, matrix.M34,
                        matrix.M40, matrix.M41, matrix.M42, matrix.M43, matrix.M44));
                }
                else
                {
                    PixelOperate(image.Data.Length, new ApplyColorMatrixOperation(data, data, mat));
                }
            }
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Applies the color matrix.
    /// </summary>
    public readonly unsafe struct ApplyColorMatrixOperation : IPixelOperation, IGpuPixelOperation<Float5x5>
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;
        private readonly ColorMatrix* _mat;

        /// <summary>
        /// Initializes a new instance of the <see cref="ApplyColorMatrixOperation"/> struct.
        /// </summary>
        /// <param name="src">The source image data.</param>
        /// <param name="dst">The destination image data.</param>
        /// <param name="matrix">The color matrix.</param>
        public ApplyColorMatrixOperation(BGRA32* src, BGRA32* dst, ColorMatrix* matrix)
        {
            _src = src;
            _dst = dst;
            _mat = matrix;
        }

        /// <inheritdoc/>
        public string GetKernel()
        {
            return "apply_matrix";
        }

        /// <inheritdoc/>
        public string GetSource()
        {
            return @"
typedef struct
{
    float R;
    float G;
    float B;
    float A;
    float W;
} ColorVector;

typedef struct
{
    float M00;
    float M01;
    float M02;
    float M03;
    float M04;
    float M10;
    float M11;
    float M12;
    float M13;
    float M14;
    float M20;
    float M21;
    float M22;
    float M23;
    float M24;
    float M30;
    float M31;
    float M32;
    float M33;
    float M34;
    float M40;
    float M41;
    float M42;
    float M43;
    float M44;
} ColorMatrix;

ColorVector Mul(ColorVector value1, ColorMatrix value2)
{
    ColorVector m;

    m.R = (value1.R * value2.M00) + (value1.G * value2.M10) + (value1.B * value2.M20) + (value1.A * value2.M30) + (value1.W * value2.M40);
    m.G = (value1.R * value2.M01) + (value1.G * value2.M11) + (value1.B * value2.M21) + (value1.A * value2.M31) + (value1.W * value2.M41);
    m.B = (value1.R * value2.M02) + (value1.G * value2.M12) + (value1.B * value2.M22) + (value1.A * value2.M32) + (value1.W * value2.M42);
    m.A = (value1.R * value2.M03) + (value1.G * value2.M13) + (value1.B * value2.M23) + (value1.A * value2.M33) + (value1.W * value2.M43);
    m.W = (value1.R * value2.M04) + (value1.G * value2.M14) + (value1.B * value2.M24) + (value1.A * value2.M34) + (value1.W * value2.M44);

    return m;
}

__kernel void apply_matrix(__global unsigned char* src, const ColorMatrix mat)
{
    int x = get_global_id(0);
    int y = get_global_id(1);
    int stride = get_global_size(0) * 4;
    int pos = stride * y + x * 4;

    ColorVector vec;
    vec.R = src[pos + 2] / 255.0f;
    vec.G = src[pos + 1] / 255.0f;
    vec.B = src[pos] / 255.0f;
    vec.A = src[pos + 3] / 255.0f;
    vec.W = 1.0f;
    vec = Mul(vec, mat);

    src[pos + 2] = (unsigned char)(vec.R * 255.0f);
    src[pos + 1] = (unsigned char)(vec.G * 255.0f);
    src[pos] = (unsigned char)(vec.B * 255.0f);
    src[pos + 3] = (unsigned char)(vec.A * 255.0f);
}";
        }

        /// <inheritdoc/>
        public void Invoke(int pos)
        {
            var pixel = _src[pos];
            var vec = new ColorVector(pixel.R / 255F, pixel.G / 255F, pixel.B / 255F, pixel.A / 255F);

            vec *= *_mat;

            _dst[pos] = new BGRA32((byte)(vec.R * 255), (byte)(vec.G * 255), (byte)(vec.B * 255), (byte)(vec.A * 255));
        }
    }
}