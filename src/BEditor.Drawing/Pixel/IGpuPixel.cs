// IGpuPixel.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Drawing.Pixel
{
    /// <summary>
    /// Represents the pixels that can be blended using the Gpu.
    /// </summary>
    /// <typeparam name="T">The type of self pixel.</typeparam>
    public interface IGpuPixel<T>
        where T : unmanaged, IPixel<T>
    {
        /// <summary>
        /// Gets the source of the OpenCL C program to be alpha-blended.
        /// </summary>
        /// <returns>Returns the source of an OpenCL C program that contains a kernel named "blend".</returns>
        public string GetBlend();

        /// <summary>
        /// Gets the source of the OpenCL C program to be alpha-blended.
        /// </summary>
        /// <returns>Returns the source of an OpenCL C program that contains a kernel named "add".</returns>
        public string GetAdd();

        /// <summary>
        /// Gets the source of the OpenCL C program to be alpha-blended.
        /// </summary>
        /// <returns>Returns the source of an OpenCL C program that contains a kernel named "subtract".</returns>
        public string Subtract();
    }
}