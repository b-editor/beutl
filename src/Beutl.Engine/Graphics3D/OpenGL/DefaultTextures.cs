namespace Beutl.Graphics.Rendering.OpenGL;

/// <summary>
/// デフォルトテクスチャファクトリー
/// </summary>
public static class DefaultTextures
{
    private static OpenGLTextureResource? _whiteTexture;
    private static OpenGLTextureResource? _blackTexture;
    private static OpenGLTextureResource? _normalTexture;
    private static OpenGLTextureResource? _defaultMetallicRoughnessTexture;

    /// <summary>
    /// 白いテクスチャ（1x1）
    /// </summary>
    public static OpenGLTextureResource WhiteTexture => _whiteTexture ??= CreateSolidColorTexture(255, 255, 255, 255);

    /// <summary>
    /// 黒いテクスチャ（1x1）
    /// </summary>
    public static OpenGLTextureResource BlackTexture => _blackTexture ??= CreateSolidColorTexture(0, 0, 0, 255);

    /// <summary>
    /// デフォルト法線マップ（1x1、法線=(0,0,1)）
    /// </summary>
    public static OpenGLTextureResource NormalTexture => _normalTexture ??= CreateSolidColorTexture(128, 128, 255, 255);

    /// <summary>
    /// デフォルトMetallic/Roughnessテクスチャ（1x1、Metallic=0, Roughness=0.5）
    /// </summary>
    public static OpenGLTextureResource DefaultMetallicRoughnessTexture => 
        _defaultMetallicRoughnessTexture ??= CreateSolidColorTexture(0, 128, 0, 255);

    private static OpenGLTextureResource CreateSolidColorTexture(byte r, byte g, byte b, byte a)
    {
        byte[] data = [r, g, b, a];
        return new OpenGLTextureResource(1, 1, TextureFormat.Rgba8, data);
    }

    /// <summary>
    /// チェッカーボードテクスチャを作成
    /// </summary>
    public static OpenGLTextureResource CreateCheckerboard(int size = 256, int checkerSize = 32)
    {
        byte[] data = new byte[size * size * 4];
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int index = (y * size + x) * 4;
                bool isWhite = ((x / checkerSize) + (y / checkerSize)) % 2 == 0;
                byte color = (byte)(isWhite ? 255 : 0);
                
                data[index + 0] = color; // R
                data[index + 1] = color; // G
                data[index + 2] = color; // B
                data[index + 3] = 255;   // A
            }
        }

        return new OpenGLTextureResource(size, size, TextureFormat.Rgba8, data);
    }

    /// <summary>
    /// グリッドテクスチャを作成
    /// </summary>
    public static OpenGLTextureResource CreateGrid(int size = 256, int gridSize = 16, byte lineWidth = 2)
    {
        byte[] data = new byte[size * size * 4];
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int index = (y * size + x) * 4;
                bool isLine = (x % gridSize < lineWidth) || (y % gridSize < lineWidth);
                byte color = (byte)(isLine ? 0 : 255);
                
                data[index + 0] = color; // R
                data[index + 1] = color; // G
                data[index + 2] = color; // B
                data[index + 3] = 255;   // A
            }
        }

        return new OpenGLTextureResource(size, size, TextureFormat.Rgba8, data);
    }

    /// <summary>
    /// 全リソースを解放
    /// </summary>
    public static void DisposeAll()
    {
        _whiteTexture?.Dispose();
        _blackTexture?.Dispose();
        _normalTexture?.Dispose();
        _defaultMetallicRoughnessTexture?.Dispose();
        
        _whiteTexture = null;
        _blackTexture = null;
        _normalTexture = null;
        _defaultMetallicRoughnessTexture = null;
    }
}
