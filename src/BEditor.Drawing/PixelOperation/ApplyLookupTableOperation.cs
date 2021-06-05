// ApplyLookupTableOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using BEditor.Compute.Memory;
using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

using static BEditor.Drawing.LookupTable;

namespace BEditor.Drawing
{
    /// <inheritdoc cref="Image"/>
    public static unsafe partial class Image
    {
        /// <summary>
        /// Adjusts the gamma of the image.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="lut">The lookup table.</param>
        /// <param name="strength">The strength.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="lut"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void ApplyLookupTable(this Image<BGRA32> image, LookupTable lut, float strength = 1)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            if (lut is null) throw new ArgumentNullException(nameof(lut));
            image.ThrowIfDisposed();

            fixed (BGRA32* data = image.Data)
            {
                if (lut.Dimension is LookupTableDimension.OneDimension)
                {
                    PixelOperate(image.Data.Length, new ApplyLookupTableOperation(data, data, lut, strength));
                }
                else
                {
                    PixelOperate(image.Data.Length, new Apply3DLookupTableOperation(data, data, lut, strength));
                }
            }
        }
    }
}

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Apply the Lookup Table.
    /// </summary>
    public readonly unsafe struct Apply3DLookupTableOperation : IPixelOperation
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;
        private readonly float* _rData;
        private readonly float* _gData;
        private readonly float* _bData;
        private readonly int _lutSize;
        private readonly float _strength;

        /// <summary>
        /// Initializes a new instance of the <see cref="Apply3DLookupTableOperation"/> struct.
        /// </summary>
        /// <param name="src">The source image data.</param>
        /// <param name="dst">The destination image data.</param>
        /// <param name="lut">The lookup table.</param>
        /// <param name="strength">The strength.</param>
        public Apply3DLookupTableOperation(BGRA32* src, BGRA32* dst, LookupTable lut, float strength)
        {
            _src = src;
            _dst = dst;
            _rData = (float*)lut.GetPointer(0);
            _gData = (float*)lut.GetPointer(1);
            _bData = (float*)lut.GetPointer(2);
            _lutSize = lut.Size;
            _strength = strength;
        }

#pragma warning disable SA1005
        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            // 1
            var color = _src[pos];
            var r = color.R * _lutSize / 256f;
            var g = color.G * _lutSize / 256f;
            var b = color.B * _lutSize / 256f;
            var vec = new Vector3(_rData[Near(r)], _gData[Near(g)], _bData[Near(b)]);

            color.R = (byte)((((vec.X * 255) + 0.5) * _strength) + (color.R * (1 - _strength)));
            color.G = (byte)((((vec.Y * 255) + 0.5) * _strength) + (color.G * (1 - _strength)));
            color.B = (byte)((((vec.Z * 255) + 0.5) * _strength) + (color.B * (1 - _strength)));

            _dst[pos] = color;

            // 2
            //var color = _src[pos];
            //var r = color.R * _lutSize / 256f;
            //var g = color.G * _lutSize / 256f;
            //var b = color.B * _lutSize / 256f;
            //var vec = new Vector3(_rData[Near(r)], _gData[Near(g)], _bData[Near(b)]);

            //Span<int> prev = stackalloc int[] { (int)r, (int)g, (int)b, };
            //Span<int> next = stackalloc int[] { Next(r), Next(g), Next(b) };
            //var d = new Float3(r - prev[0], g - prev[1], b - prev[2]);
            //var c000 = new Float3(_rData[prev[0]], _gData[prev[1]], _bData[prev[2]]);
            //var c111 = new Float3(_rData[next[0]], _gData[next[1]], _bData[next[2]]);
            //var c = default(Float3);
            //if (d.R > d.B)
            //{
            //    if (d.G > d.B)
            //    {
            //        var c100 = new Float3(_rData[next[0]], _gData[prev[1]], _bData[prev[2]]);
            //        var c110 = new Float3(_rData[next[0]], _gData[next[1]], _bData[prev[2]]);
            //        c.R = ((1 - d.R) * c000.R) + ((d.R - d.G) * c100.R) + ((d.G - d.B) * c110.R) + (d.B * c111.R);
            //        c.G = ((1 - d.R) * c000.G) + ((d.R - d.G) * c100.G) + ((d.G - d.B) * c110.G) + (d.B * c111.G);
            //        c.B = ((1 - d.R) * c000.B) + ((d.R - d.G) * c100.B) + ((d.G - d.B) * c110.B) + (d.B * c111.B);
            //    }
            //    else if (d.R > d.B)
            //    {
            //        var c100 = new Float3(_rData[next[0]], _gData[prev[1]], _bData[prev[2]]);
            //        var c101 = new Float3(_rData[next[0]], _gData[prev[1]], _bData[next[2]]);
            //        c.R = ((1 - d.R) * c000.R) + ((d.R - d.B) * c100.R) + ((d.B - d.G) * c101.R) + (d.G * c111.R);
            //        c.G = ((1 - d.R) * c000.G) + ((d.R - d.B) * c100.G) + ((d.B - d.G) * c101.G) + (d.G * c111.G);
            //        c.B = ((1 - d.R) * c000.B) + ((d.R - d.B) * c100.B) + ((d.B - d.G) * c101.B) + (d.G * c111.B);
            //    }
            //    else
            //    {
            //        var c001 = new Float3(_rData[prev[0]], _gData[prev[1]], _bData[next[2]]);
            //        var c101 = new Float3(_rData[next[0]], _gData[prev[1]], _bData[next[2]]);
            //        c.R = ((1 - d.B) * c000.R) + ((d.B - d.R) * c001.R) + ((d.R - d.G) * c101.R) + (d.G * c111.R);
            //        c.G = ((1 - d.B) * c000.G) + ((d.B - d.R) * c001.G) + ((d.R - d.G) * c101.G) + (d.G * c111.G);
            //        c.B = ((1 - d.B) * c000.B) + ((d.B - d.R) * c001.B) + ((d.R - d.G) * c101.B) + (d.G * c111.B);
            //    }
            //}
            //else
            //{
            //    if (d.B > d.G)
            //    {
            //        var c001 = new Float3(_rData[prev[0]], _gData[prev[1]], _bData[next[2]]);
            //        var c011 = new Float3(_rData[prev[0]], _gData[next[1]], _bData[next[2]]);
            //        c.R = ((1 - d.B) * c000.R) + ((d.B - d.G) * c001.R) + ((d.G - d.R) * c011.R) + (d.R * c111.R);
            //        c.G = ((1 - d.B) * c000.G) + ((d.B - d.G) * c001.G) + ((d.G - d.R) * c011.G) + (d.R * c111.G);
            //        c.B = ((1 - d.B) * c000.B) + ((d.B - d.G) * c001.B) + ((d.G - d.R) * c011.B) + (d.R * c111.B);
            //    }
            //    else if (d.B > d.R)
            //    {
            //        var c010 = new Float3(_rData[prev[0]], _gData[next[1]], _bData[prev[2]]);
            //        var c011 = new Float3(_rData[prev[0]], _gData[next[1]], _bData[next[2]]);
            //        c.R = ((1 - d.G) * c000.R) + ((d.G - d.B) * c010.R) + ((d.B - d.R) * c011.R) + (d.R * c111.R);
            //        c.G = ((1 - d.G) * c000.G) + ((d.G - d.B) * c010.G) + ((d.B - d.R) * c011.G) + (d.R * c111.G);
            //        c.B = ((1 - d.G) * c000.B) + ((d.G - d.B) * c010.B) + ((d.B - d.R) * c011.B) + (d.R * c111.B);
            //    }
            //    else
            //    {
            //        var c010 = new Float3(_rData[prev[0]], _gData[next[1]], _bData[prev[2]]);
            //        var c110 = new Float3(_rData[next[0]], _gData[next[1]], _bData[prev[2]]);
            //        c.R = ((1 - d.G) * c000.R) + ((d.G - d.R) * c010.R) + ((d.R - d.B) * c110.R) + (d.B * c111.R);
            //        c.G = ((1 - d.G) * c000.G) + ((d.G - d.R) * c010.G) + ((d.R - d.B) * c110.G) + (d.B * c111.G);
            //        c.B = ((1 - d.G) * c000.B) + ((d.G - d.R) * c010.B) + ((d.R - d.B) * c110.B) + (d.B * c111.B);
            //    }
            //}

            //color.R = (byte)((((vec.X * 255) + 0.5) * _strength) + (color.R * (1 - _strength)));
            //color.G = (byte)((((vec.Y * 255) + 0.5) * _strength) + (color.G * (1 - _strength)));
            //color.B = (byte)((((vec.Z * 255) + 0.5) * _strength) + (color.B * (1 - _strength)));

            //_dst[pos] = color;

            // 3
            //var color = _src[pos];
            //var r = color.R * _lutSize / 256f;
            //var g = color.G * _lutSize / 256f;
            //var b = color.B * _lutSize / 256f;
            //Span<int> prev = stackalloc int[] { (int)r, (int)g, (int)b };
            //Span<int> next = stackalloc int[] { Next(r), Next(g), Next(b) };
            //var d = new Float3(r - prev[0], g - prev[1], b - prev[2]);
            //var c000 = new Float3(_rData[prev[0]], _gData[prev[1]], _bData[prev[2]]);
            //var c001 = new Float3(_rData[prev[0]], _gData[prev[1]], _bData[next[2]]);
            //var c010 = new Float3(_rData[prev[0]], _gData[next[1]], _bData[prev[2]]);
            //var c011 = new Float3(_rData[prev[0]], _gData[next[1]], _bData[next[2]]);
            //var c100 = new Float3(_rData[next[0]], _gData[prev[1]], _bData[prev[2]]);
            //var c101 = new Float3(_rData[next[0]], _gData[prev[1]], _bData[next[2]]);
            //var c110 = new Float3(_rData[next[0]], _gData[next[1]], _bData[prev[2]]);
            //var c111 = new Float3(_rData[next[0]], _gData[next[1]], _bData[next[2]]);
            //var c00 = Lerp(&c000, &c100, d.R);
            //var c10 = Lerp(&c010, &c110, d.R);
            //var c01 = Lerp(&c001, &c101, d.R);
            //var c11 = Lerp(&c011, &c111, d.R);
            //var c0 = Lerp(&c00, &c10, d.G);
            //var c1 = Lerp(&c01, &c11, d.G);
            //var c = Lerp(&c0, &c1, d.G);

            //color.R = (byte)((((c.R * 255) + 0.5) * _strength) + (color.R * (1 - _strength)));
            //color.G = (byte)((((c.G * 255) + 0.5) * _strength) + (color.G * (1 - _strength)));
            //color.B = (byte)((((c.B * 255) + 0.5) * _strength) + (color.B * (1 - _strength)));

            //_dst[pos] = color;
        }
#pragma warning restore SA1005

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float LerpF(float v0, float v1, float f)
        {
            return v0 + ((v1 - v0) * f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Float3 Lerp(Float3* v0, Float3* v1, float f)
        {
            return new Float3(LerpF(v0->R, v1->R, f), LerpF(v0->G, v1->G, f), LerpF(v0->B, v1->B, f));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Next(float x)
        {
            return Math.Min((int)x + 1, _lutSize - 1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Near(float x)
        {
            return Math.Min((int)(x + .5), _lutSize - 1);
        }
    }

    /// <summary>
    /// Apply the Lookup Table.
    /// </summary>
    public readonly unsafe struct ApplyLookupTableOperation : IPixelOperation
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;
        private readonly float* _lut;
        private readonly int _lutSize;
        private readonly float _strength;

        /// <summary>
        /// Initializes a new instance of the <see cref="ApplyLookupTableOperation"/> struct.
        /// </summary>
        /// <param name="src">The source image data.</param>
        /// <param name="dst">The destination image data.</param>
        /// <param name="lut">The lookup table.</param>
        /// <param name="strength">The strength.</param>
        public ApplyLookupTableOperation(BGRA32* src, BGRA32* dst, LookupTable lut, float strength)
        {
            _src = src;
            _dst = dst;
            _lut = (float*)lut.GetPointer();
            _lutSize = lut.Size;
            _strength = strength;
        }

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            var color = _src[pos];
            var r = color.R * _lutSize / 256;
            var g = color.G * _lutSize / 256;
            var b = color.B * _lutSize / 256;

            color.R = (byte)(_lut[r] * 255 * _strength);
            color.G = (byte)(_lut[g] * 255 * _strength);
            color.B = (byte)(_lut[b] * 255 * _strength);

            _dst[pos] = color;
        }
    }
}
