// IPixelOperation.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Drawing.PixelOperation
{
    /// <summary>
    /// Represents a pixels operation.
    /// </summary>
    public interface IPixelOperation
    {
        /// <summary>
        /// Operate on a single pixel.
        /// </summary>
        /// <param name="pos">The index of the pixel to operate on.</param>
        public void Invoke(int pos);
    }
}