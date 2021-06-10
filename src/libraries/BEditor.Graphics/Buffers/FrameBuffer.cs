// FrameBuffer.cs
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
    /// Represents a frame buffer in OpenGL.
    /// </summary>
    public class FrameBuffer : IDisposable
    {
        private readonly SynchronizationContext _syncContext = AsyncOperationManager.SynchronizationContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="FrameBuffer"/> class.
        /// </summary>
        /// <param name="color">The color buffer for this frame buffer.</param>
        /// <param name="depth">The depth buffer for this frame buffer.</param>
        public FrameBuffer(ColorBuffer color, DepthBuffer depth)
        {
            if (color is null) throw new ArgumentNullException(nameof(color));
            if (depth is null) throw new ArgumentNullException(nameof(depth));

            Handle = GL.GenFramebuffer();
            (ColorObject, DepthObject) = (color, depth);
            color.Bind();
            depth.Bind();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, Handle);

            // フレームバッファオブジェクトにカラーバッファとしてテクスチャを結合する
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, color.Handle, 0);

            // フレームバッファオブジェクトにデプスバッファとしてレンダーバッファを結合する
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depth.Handle);

            // フレームバッファオブジェクトの結合を解除する
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
            GL.BindTexture(TextureTarget.Texture2D, 0);

            Tool.ThrowGLError();
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="FrameBuffer"/> class.
        /// </summary>
        ~FrameBuffer()
        {
            Dispose();
        }

        /// <summary>
        /// Gets the color buffer of this frame buffer.
        /// </summary>
        public ColorBuffer ColorObject { get; }

        /// <summary>
        /// Gets the depth buffer of this frame buffer.
        /// </summary>
        public DepthBuffer DepthObject { get; }

        /// <summary>
        /// Gets the handle of this frame buffer.
        /// </summary>
        public GraphicsHandle Handle { get; }

        /// <summary>
        /// Gets a value indicating whether this instance has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Bind this frame buffer.
        /// </summary>
        public void Bind()
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(FrameBuffer));
            var error = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, Handle);
            error = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            Tool.ThrowGLError();
        }

        /// <summary>
        /// Bind this framebuffer by specifying the target.
        /// </summary>
        /// <param name="target">The frame buffer target.</param>
        public void Bind(FramebufferTarget target)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(FrameBuffer));

            GL.BindFramebuffer(target, Handle);
            Tool.ThrowGLError();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (IsDisposed) return;

            _syncContext.Send(_ => GL.DeleteFramebuffer(Handle), null);

            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
    }
}