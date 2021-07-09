// ApplyLookupTableOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Drawing.Printing;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

using BEditor.Compute.Memory;
using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

namespace BEditor.Drawing
{
    /// <inheritdoc cref="Image"/>
    public static unsafe partial class Image
    {
        /// <summary>
        /// Apply the Lookup Table.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="lut">The lookup table.</param>
        /// <param name="strength">The strength.</param>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> or <paramref name="lut"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Apply(this Image<BGRA32> image, LookupTable lut, float strength = 1)
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
        private readonly float _scale;

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
            _scale = _lutSize / 256f;
        }

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            var color = _src[pos];
            var r = color.R * _scale;
            var g = color.G * _scale;
            var b = color.B * _scale;
            var vec = new Vector3(_rData[Near(r)], _gData[Near(g)], _bData[Near(b)]);

            color.R = (byte)((((vec.X * 255) + 0.5) * _strength) + (color.R * (1 - _strength)));
            color.G = (byte)((((vec.Y * 255) + 0.5) * _strength) + (color.G * (1 - _strength)));
            color.B = (byte)((((vec.Z * 255) + 0.5) * _strength) + (color.B * (1 - _strength)));

            _dst[pos] = color;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Near(float x)
        {
            return Math.Min((int)(x + 0.5), _lutSize - 1);
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
            var r = color.R * _lutSize / 256f;
            var g = color.G * _lutSize / 256f;
            var b = color.B * _lutSize / 256f;
            var vec = new Vector3(_lut[Near(r)], _lut[Near(g)], _lut[Near(b)]);

            color.R = (byte)((((vec.X * 255) + 0.5) * _strength) + (color.R * (1 - _strength)));
            color.G = (byte)((((vec.Y * 255) + 0.5) * _strength) + (color.G * (1 - _strength)));
            color.B = (byte)((((vec.Z * 255) + 0.5) * _strength) + (color.B * (1 - _strength)));

            _dst[pos] = color;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Near(float x)
        {
            return Math.Min((int)(x + 0.5), _lutSize - 1);
        }
    }

    internal struct Float3
    {
        public float B;
        public float G;
        public float R;

        public Float3(float r, float g, float b)
        {
            B = b;
            G = g;
            R = r;
        }
    }
}