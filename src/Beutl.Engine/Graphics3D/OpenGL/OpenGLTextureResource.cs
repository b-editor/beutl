namespace Beutl.Graphics.Rendering.OpenGL;

/// <summary>
/// OpenGL用テクスチャリソース
/// </summary>
public class OpenGLTextureResource : ITextureResource
{
    private bool _disposed;

    public uint TextureId { get; private set; }
    public int Width { get; }
    public int Height { get; }
    public TextureFormat Format { get; }

    public OpenGLTextureResource(int width, int height, TextureFormat format, ReadOnlySpan<byte> data)
    {
        Width = width;
        Height = height;
        Format = format;
        CreateTexture(data);
    }

    private void CreateTexture(ReadOnlySpan<byte> data)
    {
        TextureId = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, TextureId);

        // フォーマットをOpenGLの形式に変換
        var (internalFormat, pixelFormat, pixelType) = GetOpenGLFormat(Format);

        // テクスチャデータをアップロード
        GL.TexImage2D(TextureTarget.Texture2D, 0, internalFormat, Width, Height, 0, pixelFormat, pixelType, data.ToArray());

        // ミップマップを生成
        if (IsMipmapFormat(Format))
        {
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        }
        else
        {
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        }

        // テクスチャラップモードを設定
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

        GL.BindTexture(TextureTarget.Texture2D, 0);
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

    private static bool IsMipmapFormat(TextureFormat format)
    {
        return format is TextureFormat.Rgb8 or TextureFormat.Rgba8 or TextureFormat.R8 or TextureFormat.Rg8;
    }

    public void Bind(int textureSlot = 0)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + textureSlot);
        GL.BindTexture(TextureTarget.Texture2D, TextureId);
    }

    public void Unbind()
    {
        GL.BindTexture(TextureTarget.Texture2D, 0);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        GL.DeleteTexture(TextureId);
        TextureId = 0;
        _disposed = true;
    }
}
