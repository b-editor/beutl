// Texture.cs
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

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents an texture.
    /// </summary>
    public sealed class Texture : Drawable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Texture"/> class.
        /// </summary>
        /// <param name="impl">The texture implementaion.</param>
        public Texture(ITextureImpl impl)
            : base(impl)
        {
        }

        /// <summary>
        /// Gets the width of this <see cref="Texture"/>.
        /// </summary>
        public int Width => PlatformImpl.Width;

        /// <summary>
        /// Gets the height of this <see cref="Texture"/>.
        /// </summary>
        public int Height => PlatformImpl.Height;

        /// <summary>
        /// Gets the vertices of this <see cref="ITextureImpl"/>.
        /// </summary>
        public ReadOnlyMemory<Vector3> Vertices => PlatformImpl.Vertices;

        /// <summary>
        /// Gets the uv vertices of this <see cref="ITextureImpl"/>.
        /// </summary>
        public ReadOnlyMemory<Vector2> Uv => PlatformImpl.Uv;

        /// <summary>
        /// Gets the texture implementation.
        /// </summary>
        public new ITextureImpl PlatformImpl => (ITextureImpl)base.PlatformImpl;

        /// <summary>
        /// Create a texture from an <see cref="Image{BGRA32}"/>.
        /// </summary>
        /// <param name="image">The image to create texture.</param>
        /// <param name="vertices">The vertices.</param>
        /// <param name="uv">The uv coordinates.</param>
        /// <exception cref="GraphicsException">Platform is not set.</exception>
        /// <returns>Returns the texture created by this method.</returns>
        public static Texture FromImage(Image<BGRA32> image, Vector3[]? vertices = null, Vector2[]? uv = null)
        {
            return new Texture(IPlatform.Current.CreateTexture(image, vertices, uv));
        }

        /// <summary>
        /// Converts this texture to an image.
        /// </summary>
        /// <returns>Returns the image.</returns>
        public Image<BGRA32> ToImage()
        {
            return PlatformImpl.ToImage();
        }
    }
}