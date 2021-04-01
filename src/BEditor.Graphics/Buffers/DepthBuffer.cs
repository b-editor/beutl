using System;
using System.ComponentModel;
using System.Threading;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents the depth buffer of the frame buffer.
    /// </summary>
    public class DepthBuffer : IDisposable
    {
        private readonly SynchronizationContext _syncContext = AsyncOperationManager.SynchronizationContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="DepthBuffer"/> class.
        /// </summary>
        /// <param name="width">The width of the render buffer.</param>
        /// <param name="height">The height of the render buffer.</param>
        public DepthBuffer(int width, int height)
        {
            Handle = GL.GenRenderbuffer();
            (Width, Height) = (width, height);
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, Handle);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent, Width, Height);
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
        }

        /// <summary>
        /// Discards the reference to the target that is represented by the current <see cref="DepthBuffer"/> object.
        /// </summary>
        ~DepthBuffer()
        {
            Dispose();
        }

        /// <summary>
        /// Gets the width of the render buffer.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the height of the render buffer.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Gets the handle of the depth buffer.
        /// </summary>
        public GraphicsHandle Handle { get; }

        /// <summary>
        /// Get whether an object has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (IsDisposed) return;

            _syncContext.Post(_ => GL.DeleteRenderbuffer(Handle), null);

            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
    }
}
