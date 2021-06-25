using System;
using System.Linq;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Graphics.OpenGL
{
    public sealed unsafe class TextureImpl : GraphicsObject
    {
        private readonly VertexPositionTexture[] _vertices;
        private readonly uint[] _indices =
        {
            0, 1, 3,
            1, 2, 3,
        };

        public TextureImpl(int glHandle, int width, int height, VertexPositionTexture[]? vertices = null)
        {
            Width = width;
            Height = height;
            Handle = glHandle;

            var halfW = width / 2f;
            var halfH = height / 2f;

            vertices ??= new VertexPositionTexture[]
            {
                new(new(halfW, -halfH, 0), new(1, 1)),
                new(new(halfW, halfH, 0), new(1, 0)),
                new(new(-halfW, halfH, 0), new(0, 0)),
                new(new(-halfW, -halfH, 0), new(0, 1)),
            };
            _vertices = vertices;

            VertexArrayObject = GL.GenVertexArray();
            GL.BindVertexArray(VertexArrayObject);

            VertexBufferObject = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, VertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, _vertices.Length * sizeof(VertexPositionTexture), _vertices, BufferUsageHint.StaticDraw);

            ElementBufferObject = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ElementBufferObject);
            GL.BufferData(BufferTarget.ElementArrayBuffer, _indices.Length * sizeof(uint), _indices, BufferUsageHint.StaticDraw);
        }

        public override float[] Vertices => _vertices.SelectMany(i => i.Enumerate()).ToArray();

        public ReadOnlyMemory<uint> Indices => _indices;

        public int Width { get; }

        public int Height { get; }

        public GraphicsHandle Handle { get; }

        public GraphicsHandle ElementBufferObject { get; }

        public GraphicsHandle VertexBufferObject { get; }

        public GraphicsHandle VertexArrayObject { get; }

        /// <summary>
        /// Create a texture from an image file.
        /// </summary>
        /// <param name="path">The image files for creating textures.</param>
        /// <param name="vertices">The vertices.</param>
        /// <returns>Returns the texture created by this method.</returns>
        public static unsafe TextureImpl FromFile(string path, VertexPositionTexture[]? vertices = null)
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
        public static unsafe TextureImpl FromImage(Image<BGR24> image, VertexPositionTexture[]? vertices = null)
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

            return new TextureImpl(handle, image.Width, image.Height, vertices);
        }

        /// <summary>
        /// Create a texture from an <see cref="Image{BGRA32}"/>.
        /// </summary>
        /// <param name="image">The image to create texture.</param>
        /// <param name="vertices">The vertices.</param>
        /// <returns>Returns the texture created by this method.</returns>
        public static unsafe TextureImpl FromImage(Image<BGRA32> image, VertexPositionTexture[]? vertices = null)
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

            return new TextureImpl(handle, image.Width, image.Height, vertices);
        }

        public void Use(TextureUnit unit)
        {
            GL.ActiveTexture(unit);
            Tool.ThrowGLError();

            GL.BindTexture(TextureTarget.Texture2D, Handle);
            Tool.ThrowGLError();
        }

        public void Draw(TextureUnit unit)
        {
            GL.BindVertexArray(VertexArrayObject);
            Tool.ThrowGLError();
            Use(unit);

            GL.DrawElements(PrimitiveType.Triangles, _indices.Length, DrawElementsType.UnsignedInt, 0);
            Tool.ThrowGLError();
        }

        public override void Draw()
        {
            Draw(TextureUnit.Texture0);
        }

        protected override void Dispose(bool disposing)
        {
            GL.DeleteVertexArray(VertexArrayObject);
            GL.DeleteBuffer(VertexBufferObject);
            GL.DeleteBuffer(ElementBufferObject);
            GL.DeleteTexture(Handle);
        }
    }
}