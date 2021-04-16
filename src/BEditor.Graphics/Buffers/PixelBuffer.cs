using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Graphics
{
    public class PixelBuffer : IDisposable
    {
        private readonly SynchronizationContext _syncContext = AsyncOperationManager.SynchronizationContext;

        public PixelBuffer(int width, int height, int channel, PixelFormat format, PixelType type)
        {
            (Width, Height, Channel, Format, Type) = (width, height, channel, format, type);
            BufferSize = width * height * channel * GetPixelSize(type);

            Handle = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, Handle);
            GL.BufferData(BufferTarget.PixelUnpackBuffer, BufferSize, IntPtr.Zero, BufferUsageHint.StreamRead);
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
        }

        ~PixelBuffer()
        {
            Dispose();
        }

        public int Width { get; }

        public int Height { get; }

        public int Channel { get; }

        public int BufferSize { get; }

        public GraphicsHandle Handle { get; }

        public PixelFormat Format { get; }

        public PixelType Type { get; }

        /// <summary>
        /// Get whether an object has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        private static int GetPixelSize(PixelType type)
        {
            return type switch
            {
                PixelType.Float => sizeof(float),
                PixelType.UnsignedByte => sizeof(byte),
                _ => sizeof(int)
            };
        }

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

        public void Dispose()
        {
            if (IsDisposed) return;

            _syncContext.Post(_ =>
            {
                GL.BindBuffer(BufferTarget.ArrayBuffer, Handle);
                GL.DeleteBuffer(Handle);
            }, null);

            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
    }
}