// Negaposi3DOperation.cs
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
    public readonly unsafe struct Negaposi3DOperation : IPixelOperation
    {
        private readonly byte _red;
        private readonly byte _green;
        private readonly byte _blue;
        private readonly float* _rData;
        private readonly float* _gData;
        private readonly float* _bData;

        /// <summary>
        /// Initializes a new instance of the <see cref="Negaposi3DOperation"/> struct.
        /// </summary>
        /// <param name="red">The red threshold value.</param>
        /// <param name="green">The green threshold value.</param>
        /// <param name="blue">The blue threshold value.</param>
        /// <param name="rData">The lookup table.</param>
        /// <param name="gData">The lookup table.</param>
        /// <param name="bData">The lookup table.</param>
        public Negaposi3DOperation(byte red, byte green, byte blue, float* rData, float* gData, float* bData)
        {
            _red = red;
            _green = green;
            _blue = blue;
            _rData = rData;
            _gData = gData;
            _bData = bData;
        }

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            _rData[pos] = (_red - pos) / 256f;
            _gData[pos] = (_green - pos) / 256f;
            _bData[pos] = (_blue - pos) / 256f;
        }
    }
}