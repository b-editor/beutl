// Texture.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents an texture.
    /// </summary>
    public sealed class Texture : Drawable
    {
        private readonly Image<BGRA32> _image;

        private Texture(Image<BGRA32> image, VertexPositionTexture[]? vertices = null)
        {
            _image = image;

            var halfW = Width / 2f;
            var halfH = Height / 2f;

            Vertices = vertices ?? new VertexPositionTexture[]
            {
                new(new(halfW, -halfH, 0), new(1, 1)),
                new(new(halfW, halfH, 0), new(1, 0)),
                new(new(-halfW, halfH, 0), new(0, 0)),
                new(new(-halfW, -halfH, 0), new(0, 1)),
            };
        }

        /// <summary>
        /// Gets the width of this <see cref="Texture"/>.
        /// </summary>
        public int Width => _image.Width;

        /// <summary>
        /// Gets the height of this <see cref="Texture"/>.
        /// </summary>
        public int Height => _image.Height;

        /// <summary>
        /// Gets the vertices of this <see cref="Texture"/>.
        /// </summary>
        public VertexPositionTexture[] Vertices { get; }

        /// <summary>
        /// Create a texture from an <see cref="Image{BGRA32}"/>.
        /// </summary>
        /// <param name="image">The image to create texture.</param>
        /// <param name="vertices">The vertices.</param>
        /// <returns>Returns the texture created by this method.</returns>
        public static Texture FromImage(Image<BGRA32> image, VertexPositionTexture[]? vertices = null)
        {
            return new(image.Clone(), vertices);
        }

        /// <summary>
        /// Converts this texture to an image.
        /// </summary>
        /// <returns>Returns the image.</returns>
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