using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Graphics
{
    public class Texture : IDisposable
    {
        private readonly float[] _vertices;
        private readonly uint[] _indices =
        {
            0, 1, 3,
            1, 2, 3
        };
        private readonly SynchronizationContext? _synchronization;

        public Texture(int glHandle, int width, int height)
        {
            _synchronization = SynchronizationContext.Current;
            Debug.Assert(_synchronization is not null);

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
        ~Texture()
        {
            if (!IsDisposed) Dispose();
        }

        public ReadOnlyMemory<float> Vertices => _vertices;
        public ReadOnlyMemory<uint> Indices => _indices;
        public int Width { get; }
        public int Height { get; }
        public int Handle { get; }
        public bool IsDisposed { get; private set; }
        public int ElementBufferObject { get; }
        public int VertexBufferObject { get; }
        public int VertexArrayObject { get; }

        public unsafe static Texture FromFile(string path)
        {
            using var image = Image.Decode(path);

            return FromImage(image);
        }
        public unsafe static Texture FromImage(Image<BGR24> image)
        {
            int handle = GL.GenTexture();

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
        public unsafe static Texture FromImage(Image<BGRA32> image)
        {
            int handle = GL.GenTexture();

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

        public void Use(TextureUnit unit)
        {
            GL.ActiveTexture(unit);
            GL.BindTexture(TextureTarget.Texture2D, Handle);
        }
        public void Render(TextureUnit unit)
        {
            GL.BindVertexArray(VertexArrayObject);
            Use(unit);


            GL.DrawElements(PrimitiveType.Triangles, _indices.Length, DrawElementsType.UnsignedInt, 0);
        }

        public void Dispose()
        {
            if (IsDisposed) return;

            _synchronization?.Post(state =>
            {
                var t = (Texture)state!;
                GL.DeleteBuffer(t.VertexArrayObject);
                GL.DeleteBuffer(t.ElementBufferObject);
                GL.DeleteTexture(t.Handle);

            }, this);

            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
    }
}
