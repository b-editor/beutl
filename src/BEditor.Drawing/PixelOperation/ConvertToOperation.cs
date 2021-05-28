// ConvertToOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using BEditor.Drawing.Pixel;

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Converts the pixels.
    /// </summary>
    /// <typeparam name="T1">The type of pixel before conversion.</typeparam>
    /// <typeparam name="T2">The type of pixel after conversion.</typeparam>
    public readonly unsafe struct ConvertToOperation<T1, T2> : IPixelOperation
        where T1 : unmanaged, IPixel<T1>, IPixelConvertable<T2>
        where T2 : unmanaged, IPixel<T2>
    {
        private readonly T1* _src;
        private readonly T2* _dst;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConvertToOperation{T1, T2}"/> struct.
        /// </summary>
        /// <param name="src">The source image data.</param>
        /// <param name="dst">The destination image data.</param>
        public ConvertToOperation(T1* src, T2* dst)
        {
            _src = src;
            _dst = dst;
        }

        /// <inheritdoc/>
        public readonly void Invoke(int p)
        {
            _src[p].ConvertTo(out _dst[p]);
        }
    }
}