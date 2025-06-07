namespace Beutl.Graphics.Rendering.OpenGL;

/// <summary>
/// キューブマップテクスチャリソース
/// </summary>
public class OpenGLCubemapResource : ITextureResource
{
    private bool _disposed;

    public uint TextureId { get; private set; }
    public int Width { get; }
    public int Height { get; }
    public TextureFormat Format { get; }

    public OpenGLCubemapResource(int size, TextureFormat format, byte[][] faceData)
    {
        if (faceData.Length != 6)
            throw new ArgumentException("Cubemap requires exactly 6 faces", nameof(faceData));

        Width = size;
        Height = size;
        Format = format;
        CreateCubemap(faceData);
    }

    private void CreateCubemap(byte[][] faceData)
    {
        TextureId = GL.GenTexture();
        GL.BindTexture(TextureTarget.TextureCubeMap, TextureId);

        var (internalFormat, pixelFormat, pixelType) = OpenGLTextureResource.GetOpenGLFormat(Format);

        // 6つの面をアップロード
        for (int i = 0; i < 6; i++)
        {
            GL.TexImage2D(TextureTarget.TextureCubeMapPositiveX + i, 0, internalFormat, Width, Height, 0, pixelFormat, pixelType, faceData[i]);
        }

        // フィルタリング設定
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);

        GL.BindTexture(TextureTarget.TextureCubeMap, 0);
    }

    public void Bind(int textureSlot = 0)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + textureSlot);
        GL.BindTexture(TextureTarget.TextureCubeMap, TextureId);
    }

    public void Unbind()
    {
        GL.BindTexture(TextureTarget.TextureCubeMap, 0);
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
