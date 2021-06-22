// ITextureImpl.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Graphics.Platform
{
    /// <summary>
    /// Defines a texture implementation.
    /// </summary>
    public interface ITextureImpl : IDrawableImpl
    {
        /// <summary>
        /// Gets the width of this <see cref="ITextureImpl"/>.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the height of this <see cref="ITextureImpl"/>.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Gets the vertices of this <see cref="ITextureImpl"/>.
        /// </summary>
        public VertexPositionTexture[] Vertices { get; }

        /// <summary>
        /// Converts this texture to an image.
        /// </summary>
        /// <returns>Returns the image.</returns>
        public Image<BGRA32> ToImage();
    }
}
