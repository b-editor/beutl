// GammaOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Drawing.PixelOperation;

namespace BEditor.Drawing.LookupTables
{
    /// <summary>
    /// Creates a lookup table to adjust the gamma.
    /// </summary>
    public readonly unsafe struct GammaOperation : IPixelOperation
    {
        private readonly float _gamma;
        private readonly float* _lut;

        /// <summary>
        /// Initializes a new instance of the <see cref="GammaOperation"/> struct.
        /// </summary>
        /// <param name="gamma">The gamma. [range: 0.01-3.0].</param>
        /// <param name="lut">The lookup table.</param>
        public GammaOperation(float gamma, float* lut)
        {
            _gamma = gamma;
            _lut = lut;
        }

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            _lut[pos] = Image.Set255Round(MathF.Pow(pos / 255f, 1f / _gamma));
        }
    }
}