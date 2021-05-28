// NoiseOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Drawing.Pixel;

using static BEditor.Drawing.Image;

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Applies a noise effect.
    /// </summary>
    public readonly unsafe struct NoiseOperation : IPixelOperation
    {
        private readonly BGRA32* _src;
        private readonly BGRA32* _dst;
        private readonly byte _value;
        private readonly Random _rand;

        /// <summary>
        /// Initializes a new instance of the <see cref="NoiseOperation"/> struct.
        /// </summary>
        /// <param name="src">The source image data.</param>
        /// <param name="dst">The destination image data.</param>
        /// <param name="value">The threshold value.</param>
        /// <param name="random">The instance of the <see cref="Random"/>.</param>
        public NoiseOperation(BGRA32* src, BGRA32* dst, byte value, Random random)
        {
            _src = src;
            _dst = dst;
            _value = value;
            _rand = random;
        }

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            // ランダム値の発生
            var rnd = _rand.Next(-(_value >> 1), _value);

            _dst[pos].R = (byte)Set255(_src[pos].R + rnd);
            _dst[pos].G = (byte)Set255(_src[pos].G + rnd);
            _dst[pos].B = (byte)Set255(_src[pos].B + rnd);
            _dst[pos].A = _src[pos].A;
        }
    }
}