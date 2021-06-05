// Negaposi1DOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using BEditor.Drawing.PixelOperation;

namespace BEditor.Drawing.LookupTables
{
    /// <summary>
    /// Creates a lookup table to flip the image negative-positive.
    /// </summary>
    public readonly unsafe struct Negaposi1DOperation : IPixelOperation
    {
        private readonly byte _value;
        private readonly float* _lut;

        /// <summary>
        /// Initializes a new instance of the <see cref="Negaposi1DOperation"/> struct.
        /// </summary>
        /// <param name="value">The threshold value.</param>
        /// <param name="lut">The lookup table.</param>
        public Negaposi1DOperation(byte value, float* lut)
        {
            _value = value;
            _lut = lut;
        }

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            _lut[pos] = (_value - pos) / 256f;
        }
    }
}