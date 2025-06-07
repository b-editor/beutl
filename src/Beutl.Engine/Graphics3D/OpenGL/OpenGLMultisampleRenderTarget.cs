using OpenTK.Graphics.OpenGL;

namespace Beutl.Graphics.Rendering.OpenGL;

/// <summary>
/// マルチサンプルレンダーターゲット（アンチエイリアシング用）
/// </summary>
public class OpenGLMultisampleRenderTarget : I3DRenderTarget
{
    private bool _disposed;

    public uint FramebufferId { get; private set; }
    public uint ColorRenderbufferId { get; private set; }
    public uint DepthRenderbufferId { get; private set; }
    public int Width { get; }
    public int Height { get; }
    public TextureFormat ColorFormat { get; }
    public TextureFormat? DepthFormat { get; }
    public int Samples { get; }

    public OpenGLMultisampleRenderTarget(int width, int height, int samples, TextureFormat colorFormat, TextureFormat? depthFormat = null)
    {
        Width = width;
        Height = height;
        Samples = Math.Max(1, samples);
        ColorFormat = colorFormat;
        DepthFormat = depthFormat ?? TextureFormat.Depth24;
        
        CreateFramebuffer();
    }

    private void CreateFramebuffer()
    {
        FramebufferId = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, FramebufferId);

        // マルチサンプルカラーレンダーバッファ
        ColorRenderbufferId = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, ColorRenderbufferId);
        var (colorInternalFormat, _, _) = OpenGLRenderTarget.GetOpenGLFormat(ColorFormat);
        GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, Samples, (RenderbufferStorage)colorInternalFormat, Width, Height);
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, ColorRenderbufferId);

        // マルチサンプルデプスレンダーバッファ
        if (DepthFormat.HasValue)
        {
            DepthRenderbufferId = GL.GenRenderbuffer();
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, DepthRenderbufferId);
            var (depthInternalFormat, _, _) = OpenGLRenderTarget.GetOpenGLFormat(DepthFormat.Value);
            GL.RenderbufferStorageMultisample(RenderbufferTarget.Renderbuffer, Samples, (RenderbufferStorage)depthInternalFormat, Width, Height);
            
            var attachment = DepthFormat.Value == TextureFormat.Depth24Stencil8 ? FramebufferAttachment.DepthStencilAttachment : FramebufferAttachment.DepthAttachment;
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, attachment, RenderbufferTarget.Renderbuffer, DepthRenderbufferId);
        }

        if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
        {
            throw new InvalidOperationException("Multisample framebuffer not complete");
        }

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Bind()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, FramebufferId);
        GL.Viewport(0, 0, Width, Height);
    }

    public void Unbind()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <summary>
    /// 通常のレンダーターゲットに解決（resolve）
    /// </summary>
    public void ResolveTo(OpenGLRenderTarget target)
    {
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, FramebufferId);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, target.FramebufferId);

        GL.BlitFramebuffer(
            0, 0, Width, Height,
            0, 0, target.Width, target.Height,
            ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit,
            BlitFramebufferFilter.Nearest);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        GL.DeleteFramebuffer(FramebufferId);
        if (ColorRenderbufferId != 0)
            GL.DeleteRenderbuffer(ColorRenderbufferId);
        if (DepthRenderbufferId != 0)
            GL.DeleteRenderbuffer(DepthRenderbufferId);

        FramebufferId = 0;
        ColorRenderbufferId = 0;
        DepthRenderbufferId = 0;

        _disposed = true;
    }
}
