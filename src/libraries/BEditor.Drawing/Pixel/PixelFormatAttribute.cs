// PixelFormatAttribute.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Drawing.Pixel
{
    /// <summary>
    /// Indicates the format information of the pixel.
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct)]
    public class PixelFormatAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PixelFormatAttribute"/> class.
        /// </summary>
        /// <param name="channels">The number of channels in the pixel.</param>
        public PixelFormatAttribute(int channels)
        {
            Channels = channels;
        }

        /// <summary>
        /// Gets the number of channels in the pixel.
        /// </summary>
        public int Channels { get; }
    }
}