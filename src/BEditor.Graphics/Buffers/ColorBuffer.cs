// ColorBuffer.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.ComponentModel;
using System.Threading;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents the color buffer of the frame buffer.
    /// </summary>
    public class ColorBuffer : IDisposable
    {
        private readonly SynchronizationContext _syncContext = AsyncOperationManager.SynchronizationContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="ColorBuffer"/> class.
        /// </summary>
        /// <param name="width">The width of the texture.</param>
        /// <param name="height">The height of the texture.</param>
        /// <param name="internalFormat">The format of the texture.</param>
        /// <param name="format">The format of the texture.</param>
        /// <param name="type">The type of the texture.</param>
        public ColorBuffer(int width, int height, PixelInternalFormat internalFormat, PixelFormat format, PixelType type)
        {
            Handle = GL.GenTexture();
            (Width, Height, InternalFormat, Format, Type) = (width, height, internalFormat, format, type);
            GL.BindTexture(TextureTarget.Texture2D, Handle);
            GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, width, height, 0, format, type, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMinFilter.Linear);
            GL.BindTexture(TextureTarget.Texture2D, 0);

            Tool.ThrowGLError();
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="ColorBuffer"/> class.
        /// </summary>
        ~ColorBuffer()
        {
            Dispose();
        }

        /// <summary>
        /// Gets the width of the texture.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the height of the texture.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Gets the format of the texture.
        /// </summary>
        public PixelInternalFormat InternalFormat { get; }

        /// <summary>
        /// Gets the format of the texture.
        /// </summary>
        public PixelFormat Format { get; }

        /// <summary>
        /// Gets the type of the texture.
        /// </summary>
        public PixelType Type { get; }

        /// <summary>
        /// Gets the handle of the color buffer.
        /// </summary>
        public GraphicsHandle Handle { get; }

        /// <summary>
        /// Get whether an object has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Bind this color buffer.
        /// </summary>
        public void Bind()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(ColorBuffer));

            GL.BindTexture(TextureTarget.Texture2D, Handle);
            Tool.ThrowGLError();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (IsDisposed) return;

            _syncContext.Send(_ => GL.DeleteTexture(Handle), null);

            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
    }
}