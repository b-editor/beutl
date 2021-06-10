// PixelBuffer.cs
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
    /// Represents the OpenGL pixel buffer.
    /// </summary>
    public class PixelBuffer : IDisposable
    {
        private readonly SynchronizationContext _syncContext = AsyncOperationManager.SynchronizationContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="PixelBuffer"/> class.
        /// </summary>
        /// <param name="width">The width of the pixel buffer.</param>
        /// <param name="height">The height of the pixel buffer.</param>
        /// <param name="channel">The number of channels in the pixel buffer.</param>
        /// <param name="format">The pixel format of the pixel buffer.</param>
        /// <param name="type">The pixel type of the pixel buffer.</param>
        public PixelBuffer(int width, int height, int channel, PixelFormat format, PixelType type)
        {
            (Width, Height, Channel, Format, Type) = (width, height, channel, format, type);
            BufferSize = width * height * channel * GetPixelSize(type);

            Handle = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, Handle);
            GL.BufferData(BufferTarget.PixelUnpackBuffer, BufferSize, IntPtr.Zero, BufferUsageHint.StreamRead);
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);

            Tool.ThrowGLError();
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="PixelBuffer"/> class.
        /// </summary>
        ~PixelBuffer()
        {
            Dispose();
        }

        /// <summary>
        /// Gets the width of this <see cref="PixelBuffer"/>.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the height of this <see cref="PixelBuffer"/>.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Gets the number of channels in this <see cref="PixelBuffer"/>.
        /// </summary>
        public int Channel { get; }

        /// <summary>
        /// Gets the buffer size of this <see cref="PixelBuffer"/>.
        /// </summary>
        public int BufferSize { get; }

        /// <summary>
        /// Gets the OpenGL handle of this <see cref="PixelBuffer"/>.
        /// </summary>
        public GraphicsHandle Handle { get; }

        /// <summary>
        /// Gets the pixel format of this <see cref="PixelBuffer"/>.
        /// </summary>
        public PixelFormat Format { get; }

        /// <summary>
        /// Gets the pixel type of this <see cref="PixelBuffer"/>.
        /// </summary>
        public PixelType Type { get; }

        /// <summary>
        /// Gets a value indicating whether this instance has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Read pixels from a texture.
        /// </summary>
        /// <param name="handle">The handle of the texture to read.</param>
        /// <param name="data">The address to write to.</param>
        public unsafe void ReadPixelsFromTexture(GraphicsHandle handle, IntPtr data)
        {
            GL.BindBuffer(BufferTarget.PixelPackBuffer, Handle);
            GL.BindTexture(TextureTarget.Texture2D, handle);
            GL.GetTexImage(TextureTarget.Texture2D, 0, Format, Type, IntPtr.Zero);

            var pboPtr = (byte*)GL.MapBuffer(BufferTarget.PixelPackBuffer, BufferAccess.ReadOnly);

            if (pboPtr is not null)
            {
                System.Buffer.MemoryCopy(pboPtr, (void*)data, BufferSize, BufferSize);
                GL.UnmapBuffer(BufferTarget.PixelPackBuffer);
            }

            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.BindBuffer(BufferTarget.PixelPackBuffer, 0);
        }

        /// <summary>
        /// Read pixels from a buffer.
        /// </summary>
        /// <param name="handle">The handle of the buffer to read.</param>
        /// <param name="data">The address to write to.</param>
        public unsafe void ReadPixelsFromBuffer(GraphicsHandle handle, IntPtr data)
        {
            GL.BindBuffer(BufferTarget.PixelPackBuffer, Handle);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, handle);
            GL.ReadPixels(0, 0, Width, Height, Format, Type, data);

            var pboPtr = (byte*)GL.MapBuffer(BufferTarget.PixelPackBuffer, BufferAccess.ReadOnly);

            if (pboPtr is not null)
            {
                System.Buffer.MemoryCopy(pboPtr, (void*)data, BufferSize, BufferSize);
                GL.UnmapBuffer(BufferTarget.PixelPackBuffer);
            }

            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
            GL.BindBuffer(BufferTarget.PixelPackBuffer, 0);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (IsDisposed) return;

            _syncContext.Send(_ =>
            {
                GL.BindBuffer(BufferTarget.ArrayBuffer, Handle);
                GL.DeleteBuffer(Handle);
            }, null);

            GC.SuppressFinalize(this);

            IsDisposed = true;
        }

        private static int GetPixelSize(PixelType type)
        {
            return type switch
            {
                PixelType.Float => sizeof(float),
                PixelType.UnsignedByte => sizeof(byte),
                PixelType.Byte => sizeof(sbyte),
                PixelType.Short => sizeof(short),
                PixelType.UnsignedShort => sizeof(ushort),
                PixelType.Int => sizeof(int),
                PixelType.UnsignedInt => sizeof(uint),
                PixelType.HalfFloat => sizeof(float) / 2,
                _ => sizeof(int),
            };
        }
    }
}