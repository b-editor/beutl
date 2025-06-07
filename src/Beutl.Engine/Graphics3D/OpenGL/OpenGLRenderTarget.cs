using Microsoft.Extensions.Logging;
using OpenTK.Graphics.OpenGL;
using Beutl.Logging;

namespace Beutl.Graphics.Rendering.OpenGL;

/// <summary>
/// OpenGL用レンダーターゲット
/// </summary>
public class OpenGLRenderTarget : I3DRenderTarget
{
    private static readonly ILogger s_logger = Log.CreateLogger<OpenGLRenderTarget>();

    private bool _disposed;

    public uint FramebufferId { get; private set; }
    public uint ColorTextureId { get; private set; }
    public uint DepthTextureId { get; private set; }
    public int Width { get; }
    public int Height { get; }
    public TextureFormat ColorFormat { get; }
    public TextureFormat? DepthFormat { get; }

    public OpenGLRenderTarget(int width, int height, TextureFormat colorFormat, TextureFormat? depthFormat = null)
    {
        Width = width;
        Height = height;
        ColorFormat = colorFormat;
        DepthFormat = depthFormat ?? TextureFormat.Depth24;

        CreateFramebuffer();
    }

    private void CreateFramebuffer()
    {
        // フレームバッファを作成
        FramebufferId = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, FramebufferId);

        // カラーテクスチャを作成
        CreateColorTexture();

        // デプステクスチャを作成
        if (DepthFormat.HasValue)
        {
            CreateDepthTexture();
        }

        // フレームバッファの完成度をチェック
        var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            s_logger.LogError("Framebuffer not complete: {Status}", status);
            throw new InvalidOperationException($"Framebuffer not complete: {status}");
        }

        // フレームバッファをアンバインド
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        s_logger.LogDebug("Created render target: {Width}x{Height}, Color: {ColorFormat}, Depth: {DepthFormat}",
            Width, Height, ColorFormat, DepthFormat);
    }

    private void CreateColorTexture()
    {
        ColorTextureId = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, ColorTextureId);

        var (internalFormat, pixelFormat, pixelType) = GetOpenGLFormat(ColorFormat);

        GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, Width, Height, 0, pixelFormat, pixelType, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, ColorTextureId, 0);
    }

    private void CreateDepthTexture()
    {
        if (!DepthFormat.HasValue)
            return;

        DepthTextureId = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, DepthTextureId);

        var (internalFormat, pixelFormat, pixelType) = GetOpenGLFormat(DepthFormat.Value);

        GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, Width, Height, 0, pixelFormat, pixelType, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        var attachment = IsStencilFormat(DepthFormat.Value) ? FramebufferAttachment.DepthStencilAttachment : FramebufferAttachment.DepthAttachment;
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, attachment, TextureTarget.Texture2D, DepthTextureId, 0);
    }

    public static (PixelInternalFormat internalFormat, PixelFormat pixelFormat, PixelType pixelType) GetOpenGLFormat(TextureFormat format)
    {
        return format switch
        {
            TextureFormat.R8 => (PixelInternalFormat.R8, PixelFormat.Red, PixelType.UnsignedByte),
            TextureFormat.Rg8 => (PixelInternalFormat.Rg8, PixelFormat.Rg, PixelType.UnsignedByte),
            TextureFormat.Rgb8 => (PixelInternalFormat.Rgb8, PixelFormat.Rgb, PixelType.UnsignedByte),
            TextureFormat.Rgba8 => (PixelInternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte),
            TextureFormat.R16F => (PixelInternalFormat.R16f, PixelFormat.Red, PixelType.HalfFloat),
            TextureFormat.Rg16F => (PixelInternalFormat.Rg16f, PixelFormat.Rg, PixelType.HalfFloat),
            TextureFormat.Rgb16F => (PixelInternalFormat.Rgb16f, PixelFormat.Rgb, PixelType.HalfFloat),
            TextureFormat.Rgba16F => (PixelInternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.HalfFloat),
            TextureFormat.R32F => (PixelInternalFormat.R32f, PixelFormat.Red, PixelType.Float),
            TextureFormat.Rg32F => (PixelInternalFormat.Rg32f, PixelFormat.Rg, PixelType.Float),
            TextureFormat.Rgb32F => (PixelInternalFormat.Rgb32f, PixelFormat.Rgb, PixelType.Float),
            TextureFormat.Rgba32F => (PixelInternalFormat.Rgba32f, PixelFormat.Rgba, PixelType.Float),
            TextureFormat.Depth16 => (PixelInternalFormat.DepthComponent16, PixelFormat.DepthComponent, PixelType.UnsignedShort),
            TextureFormat.Depth24 => (PixelInternalFormat.DepthComponent24, PixelFormat.DepthComponent, PixelType.UnsignedInt),
            TextureFormat.Depth32F => (PixelInternalFormat.DepthComponent32f, PixelFormat.DepthComponent, PixelType.Float),
            TextureFormat.Depth24Stencil8 => (PixelInternalFormat.Depth24Stencil8, PixelFormat.DepthStencil, PixelType.UnsignedInt248),
            _ => throw new ArgumentException($"Unsupported texture format: {format}")
        };
    }

    private static bool IsStencilFormat(TextureFormat format)
    {
        return format == TextureFormat.Depth24Stencil8;
    }

    /// <summary>
    /// レンダーターゲットをアクティブにする
    /// </summary>
    public void Bind()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, FramebufferId);
        GL.Viewport(0, 0, Width, Height);
    }

    /// <summary>
    /// レンダーターゲットを非アクティブにする
    /// </summary>
    public void Unbind()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <summary>
    /// カラーテクスチャをバインド
    /// </summary>
    public void BindColorTexture(int textureSlot = 0)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + textureSlot);
        GL.BindTexture(TextureTarget.Texture2D, ColorTextureId);
    }

    /// <summary>
    /// デプステクスチャをバインド
    /// </summary>
    public void BindDepthTexture(int textureSlot = 0)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + textureSlot);
        GL.BindTexture(TextureTarget.Texture2D, DepthTextureId);
    }

    /// <summary>
    /// カラーバッファをクリア
    /// </summary>
    public void ClearColor(float r = 0.0f, float g = 0.0f, float b = 0.0f, float a = 1.0f)
    {
        Bind();
        GL.ClearColor(r, g, b, a);
        GL.Clear(ClearBufferMask.ColorBufferBit);
    }

    /// <summary>
    /// デプスバッファをクリア
    /// </summary>
    public void ClearDepth(float depth = 1.0f)
    {
        Bind();
        GL.ClearDepth(depth);
        GL.Clear(ClearBufferMask.DepthBufferBit);
    }

    /// <summary>
    /// カラーとデプスバッファをクリア
    /// </summary>
    public void Clear(float r = 0.0f, float g = 0.0f, float b = 0.0f, float a = 1.0f, float depth = 1.0f)
    {
        Bind();
        GL.ClearColor(r, g, b, a);
        GL.ClearDepth(depth);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
    }

    /// <summary>
    /// 別のレンダーターゲットからコピー（Blit）
    /// </summary>
    public void BlitFrom(OpenGLRenderTarget source, bool copyColor = true, bool copyDepth = false)
    {
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, source.FramebufferId);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, FramebufferId);

        ClearBufferMask mask = 0;
        if (copyColor) mask |= ClearBufferMask.ColorBufferBit;
        if (copyDepth) mask |= ClearBufferMask.DepthBufferBit;

        GL.BlitFramebuffer(
            0, 0, source.Width, source.Height,
            0, 0, Width, Height,
            mask, BlitFramebufferFilter.Linear);

        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <summary>
    /// レンダーターゲットのサイズを変更
    /// </summary>
    public OpenGLRenderTarget Resize(int newWidth, int newHeight)
    {
        // 新しいレンダーターゲットを作成して返す
        return new OpenGLRenderTarget(newWidth, newHeight, ColorFormat, DepthFormat);
    }

    /// <summary>
    /// カラーテクスチャのピクセルデータを読み取り
    /// </summary>
    public unsafe byte[] ReadColorPixels()
    {
        Bind();

        var (_, pixelFormat, pixelType) = GetOpenGLFormat(ColorFormat);
        int bytesPerPixel = GetBytesPerPixel(ColorFormat);
        byte[] pixels = new byte[Width * Height * bytesPerPixel];

        fixed (byte* ptr = pixels)
        {
            GL.ReadPixels(0, 0, Width, Height, pixelFormat, pixelType, (IntPtr)ptr);
        }

        return pixels;
    }

    private static int GetBytesPerPixel(TextureFormat format)
    {
        return format switch
        {
            TextureFormat.R8 => 1,
            TextureFormat.Rg8 => 2,
            TextureFormat.Rgb8 => 3,
            TextureFormat.Rgba8 => 4,
            TextureFormat.R16F => 2,
            TextureFormat.Rg16F => 4,
            TextureFormat.Rgb16F => 6,
            TextureFormat.Rgba16F => 8,
            TextureFormat.R32F => 4,
            TextureFormat.Rg32F => 8,
            TextureFormat.Rgb32F => 12,
            TextureFormat.Rgba32F => 16,
            _ => 4
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        GL.DeleteFramebuffer(FramebufferId);
        if (ColorTextureId != 0)
            GL.DeleteTexture(ColorTextureId);
        if (DepthTextureId != 0)
            GL.DeleteTexture(DepthTextureId);

        FramebufferId = 0;
        ColorTextureId = 0;
        DepthTextureId = 0;

        _disposed = true;

        s_logger.LogDebug("Disposed render target: {Width}x{Height}", Width, Height);
    }
}
