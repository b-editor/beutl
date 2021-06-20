// TextureImpl.cs
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
using BEditor.Graphics.Platform;

namespace BEditor.Graphics.Skia
{
    /// <summary>
    /// The texture implementation.
    /// </summary>
    public sealed class TextureImpl : DrawableImpl, ITextureImpl
    {
        private readonly Image<BGRA32> _image;

        /// <summary>
        /// Initializes a new instance of the <see cref="TextureImpl"/> class.
        /// </summary>
        /// <param name="image">The image.</param>
        public TextureImpl(Image<BGRA32> image)
        {
            _image = image.Clone();
        }

        /// <inheritdoc/>
        public int Width => _image.Width;

        /// <inheritdoc/>
        public int Height => _image.Height;

        /// <inheritdoc/>
        public ReadOnlyMemory<VertexPositionTexture> Vertices => throw new PlatformNotSupportedException();

        /// <inheritdoc/>
        public Image<BGRA32> ToImage()
        {
            return _image.Clone();
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _image.Dispose();
        }
    }
}
