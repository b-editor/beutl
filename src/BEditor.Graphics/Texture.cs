// Texture.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents an OpenGL texture.
    /// </summary>
    public class Texture : GraphicsObject
    {
        private readonly float[] _vertices;
        private readonly uint[] _indices =
        {
            0, 1, 3,
            1, 2, 3,
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="Texture"/> class.
        /// </summary>
        /// <param name="glHandle">The handle for the texture.</param>
        /// <param name="width">The width of the texture.</param>
        /// <param name="height">The height of the texture.</param>
        /// <param name="vertices">The vertices.</param>
        public Texture(int glHandle, int width, int height, float[]? vertices = null)
        {
            Width = width;
            Height = height;
            Handle = glHandle;

            var h = width / 2f;
            var v = height / 2f;

#pragma warning disable SA1137 // Elements should have the same indentation
            _vertices = vertices ?? new float[]
            {
                 h, -v, 0.0f,  1.0f, 1.0f,
                 h,  v, 0.0f,  1.0f, 0.0f,
                -h,  v, 0.0f,  0.0f, 0.0f,
                -h, -v, 0.0f,  0.0f, 1.0f,
            };
#pragma warning restore SA1137 // Elements should have the same indentation

            VertexArrayObject = GL.GenVertexArray();
            GL.BindVertexArray(VertexArrayObject);

            VertexBufferObject = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, VertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, _vertices.Length * sizeof(float), _vertices, BufferUsageHint.StaticDraw);

            ElementBufferObject = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ElementBufferObject);
            GL.BufferData(BufferTarget.ElementArrayBuffer, _indices.Length * sizeof(uint), _indices, BufferUsageHint.StaticDraw);
        }

        /// <inheritdoc/>
        public override ReadOnlyMemory<float> Vertices => _vertices;

        /// <summary>
        /// Gets the indeces of this <see cref="Texture"/>.
        /// </summary>
        public ReadOnlyMemory<uint> Indices => _indices;

        /// <summary>
        /// Gets the width of this <see cref="Texture"/>.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the height of this <see cref="Texture"/>.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Gets the handle of this <see cref="Texture"/>.
        /// </summary>
        public GraphicsHandle Handle { get; }

        /// <summary>
        /// Gets the ElementBuffer of this <see cref="Texture"/>.
        /// </summary>
        public GraphicsHandle ElementBufferObject { get; }

        /// <summary>
        /// Gets the VertexBuffer of this <see cref="Texture"/>.
        /// </summary>
        public GraphicsHandle VertexBufferObject { get; }

        /// <summary>
        /// Gets the VertexArray of this <see cref="Texture"/>.
        /// </summary>
        public GraphicsHandle VertexArrayObject { get; }

        /// <summary>
        /// Create a texture from an image file.
        /// </summary>
        /// <param name="path">The image files for creating textures.</param>
        /// <param name="vertices">The vertices.</param>
        /// <returns>Returns the texture created by this method.</returns>
        public static unsafe Texture FromFile(string path, float[]? vertices = null)
        {
            using var image = Image.Decode(path);

            return FromImage(image, vertices);
        }

        /// <summary>
        /// Create a texture from an <see cref="Image{BGR24}"/>.
        /// </summary>
        /// <param name="image">The image to create texture.</param>
        /// <param name="vertices">The vertices.</param>
        /// <returns>Returns the texture created by this method.</returns>
        public static unsafe Texture FromImage(Image<BGR24> image, float[]? vertices = null)
        {
            var handle = GL.GenTexture();

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, handle);

            fixed (BGR24* data = image.Data)
            {
                GL.TexImage2D(TextureTarget.Texture2D,
                    0,
                    PixelInternalFormat.Rgb,
                    image.Width,
                    image.Height,
                    0,
                    PixelFormat.Bgr,
                    PixelType.UnsignedByte,
                    (IntPtr)data);
            }

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);

            return new Texture(handle, image.Width, image.Height, vertices);
        }

        /// <summary>
        /// Create a texture from an <see cref="Image{BGRA32}"/>.
        /// </summary>
        /// <param name="image">The image to create texture.</param>
        /// <param name="vertices">The vertices.</param>
        /// <returns>Returns the texture created by this method.</returns>
        public static unsafe Texture FromImage(Image<BGRA32> image, float[]? vertices = null)
        {
            var handle = GL.GenTexture();

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, handle);

            fixed (BGRA32* data = image.Data)
            {
                GL.TexImage2D(TextureTarget.Texture2D,
                    0,
                    PixelInternalFormat.Rgba,
                    image.Width,
                    image.Height,
                    0,
                    PixelFormat.Bgra,
                    PixelType.UnsignedByte,
                    (IntPtr)data);
            }

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);

            return new Texture(handle, image.Width, image.Height, vertices);
        }

        /// <summary>
        /// Use this texture.
        /// </summary>
        /// <param name="unit">The texure unit.</param>
        public void Use(TextureUnit unit)
        {
            GL.ActiveTexture(unit);
            Tool.ThrowGLError();

            GL.BindTexture(TextureTarget.Texture2D, Handle);
            Tool.ThrowGLError();
        }

        /// <inheritdoc cref="GraphicsObject.Draw"/>
        public void Draw(TextureUnit unit)
        {
            GL.BindVertexArray(VertexArrayObject);
            Tool.ThrowGLError();
            Use(unit);

            GL.DrawElements(PrimitiveType.Triangles, _indices.Length, DrawElementsType.UnsignedInt, 0);
            Tool.ThrowGLError();
        }

        /// <inheritdoc/>
        public override void Draw()
        {
            Draw(TextureUnit.Texture0);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            GL.DeleteVertexArray(VertexArrayObject);
            GL.DeleteBuffer(VertexBufferObject);
            GL.DeleteBuffer(ElementBufferObject);
            GL.DeleteTexture(Handle);
        }
    }
}