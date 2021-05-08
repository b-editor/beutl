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
            1, 2, 3
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="Texture"/> class.
        /// </summary>
        /// <param name="glHandle">The handle for the texture.</param>
        /// <param name="width">The width of the texture.</param>
        /// <param name="height">The height of the texture.</param>
        public Texture(int glHandle, int width, int height)
        {
            Width = width;
            Height = height;
            Handle = glHandle;

            var h = width / 2f;
            var v = height / 2f;

            _vertices = new float[]
            {
                 h, -v, 0.0f,  1.0f, 1.0f,
                 h,  v, 0.0f,  1.0f, 0.0f,
                -h,  v, 0.0f,  0.0f, 0.0f,
                -h, -v, 0.0f,  0.0f, 1.0f,
            };
            //Vertices = new float[]
            //{
            //     h,  v, 0.0f,  1.0f, 1.0f,
            //     h, -v, 0.0f,  1.0f, 0.0f,
            //    -h, -v, 0.0f,  0.0f, 0.0f,
            //    -h,  v, 0.0f,  0.0f, 1.0f,
            //};

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
        /// Gets the handle of this <see cref="Texture"/>
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
        public unsafe static Texture FromFile(string path)
        {
            using var image = Image.Decode(path);

            return FromImage(image);
        }

        /// <summary>
        /// Create a texture from an <see cref="Image{BGR24}"/>.
        /// </summary>
        public unsafe static Texture FromImage(Image<BGR24> image)
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

            return new Texture(handle, image.Width, image.Height);
        }

        /// <summary>
        /// Create a texture from an <see cref="Image{BGRA32}"/>.
        /// </summary>
        public unsafe static Texture FromImage(Image<BGRA32> image)
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

            return new Texture(handle, image.Width, image.Height);
        }

        /// <summary>
        /// Use this texture.
        /// </summary>
        public void Use(TextureUnit unit)
        {
            GL.ActiveTexture(unit);
            GL.BindTexture(TextureTarget.Texture2D, Handle);
        }

        /// <inheritdoc cref="GraphicsObject.Draw"/>
        public void Draw(TextureUnit unit)
        {
            GL.BindVertexArray(VertexArrayObject);
            Use(unit);

            GL.DrawElements(PrimitiveType.Triangles, _indices.Length, DrawElementsType.UnsignedInt, 0);
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