using Beutl.Threading;
using OpenTK.Graphics.OpenGL;

namespace Beutl.Graphics3D;

public sealed class FrameBuffer : IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.Current;

    public FrameBuffer(ColorBuffer color, DepthBuffer depth)
    {
        if (_dispatcher is null) throw new InvalidOperationException("Dispatcher is not initialized.");
        if (color is null) throw new ArgumentNullException(nameof(color));
        if (depth is null) throw new ArgumentNullException(nameof(depth));

        Handle = GL.GenFramebuffer();
        (ColorObject, DepthObject) = (color, depth);
        color.Bind();
        depth.Bind();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, Handle);

        // フレームバッファオブジェクトにカラーバッファとしてテクスチャを結合する
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2d, color.Handle, 0);

        // フレームバッファオブジェクトにデプスバッファとしてレンダーバッファを結合する
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment,
            RenderbufferTarget.Renderbuffer, depth.Handle);

        // フレームバッファのチェック
        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status is not FramebufferStatus.FramebufferComplete)
        {
            throw new Exception("Framebuffer is not complete.");
        }

        // フレームバッファオブジェクトの結合を解除する
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
        GL.BindTexture(TextureTarget.Texture2d, 0);

        GlErrorHelper.CheckGlError();
    }

    ~FrameBuffer()
    {
        Dispose();
    }

    public ColorBuffer ColorObject { get; }

    public DepthBuffer DepthObject { get; }

    public int Handle { get; }

    public bool IsDisposed { get; private set; }

    public void Bind()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(FrameBuffer));
        var error = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, Handle);
        error = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        GlErrorHelper.CheckGlError();
    }

    public void Bind(FramebufferTarget target)
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(FrameBuffer));

        GL.BindFramebuffer(target, Handle);
        GlErrorHelper.CheckGlError();
    }

    public void Unbind()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GlErrorHelper.CheckGlError();
    }

    public void Unbind(FramebufferTarget target)
    {
        GL.BindFramebuffer(target, 0);
        GlErrorHelper.CheckGlError();
    }

    public void Dispose()
    {
        if (IsDisposed) return;

        _dispatcher.Run(() => GL.DeleteFramebuffer(Handle));

        GC.SuppressFinalize(this);

        IsDisposed = true;
    }
}
