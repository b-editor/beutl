// AddOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Add and combine the pixels.
    /// </summary>
    /// <typeparam name="T">The type of pixel.</typeparam>
    public readonly unsafe struct AddOperation<T> : IPixelOperation
        where T : unmanaged, IPixel<T>
    {
        private readonly T* _src1;
        private readonly T* _src2;
        private readonly T* _dst;

        /// <summary>
        /// Initializes a new instance of the <see cref="AddOperation{T}"/> struct.
        /// </summary>
        /// <param name="src1">The first source image data.</param>
        /// <param name="src2">The second source image data.</param>
        /// <param name="dst">The destination image data.</param>
        public AddOperation(T* src1, T* src2, T* dst)
        {
            _src1 = src1;
            _src2 = src2;
            _dst = dst;
        }

        /// <inheritdoc/>
        public readonly void Invoke(int pos)
        {
            _dst[pos] = _src1[pos].Add(_src2[pos]);
        }
    }
}