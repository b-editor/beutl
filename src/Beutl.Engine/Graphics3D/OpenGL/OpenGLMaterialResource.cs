namespace Beutl.Graphics.Rendering.OpenGL;

/// <summary>
/// OpenGL用マテリアルリソース
/// </summary>
public class OpenGLMaterialResource : I3DMaterialResource
{
    private bool _disposed;

    public I3DMaterial SourceMaterial { get; }
    public ITextureResource? AlbedoTexture { get; }
    public ITextureResource? NormalTexture { get; }
    public ITextureResource? MetallicRoughnessTexture { get; }

    public OpenGLMaterialResource(I3DMaterial material, OpenGLRenderer renderer)
    {
        SourceMaterial = material;

        // テクスチャを作成またはデフォルトテクスチャを使用
        AlbedoTexture = material.AlbedoTexture != null
            ? CreateTextureResource(material.AlbedoTexture, renderer)
            : DefaultTextures.WhiteTexture;

        NormalTexture = material.NormalTexture != null
            ? CreateTextureResource(material.NormalTexture, renderer)
            : DefaultTextures.NormalTexture;

        MetallicRoughnessTexture = material.MetallicRoughnessTexture != null
            ? CreateTextureResource(material.MetallicRoughnessTexture, renderer)
            : DefaultTextures.DefaultMetallicRoughnessTexture;
    }

    private static ITextureResource CreateTextureResource(ITexture texture, OpenGLRenderer renderer)
    {
        // 実際の実装では、ITextureからOpenGLTextureResourceに変換
        // 簡略化のためデフォルトテクスチャを返す
        return DefaultTextures.WhiteTexture;
    }

    /// <summary>
    /// マテリアルテクスチャをバインド
    /// </summary>
    public void Bind()
    {
        // アルベドテクスチャ (slot 0)
        ((OpenGLTextureResource)AlbedoTexture!).Bind(0);

        // 法線テクスチャ (slot 1)
        ((OpenGLTextureResource)NormalTexture!).Bind(1);

        // メタリック/ラフネステクスチャ (slot 2)
        ((OpenGLTextureResource)MetallicRoughnessTexture!).Bind(2);
    }

    /// <summary>
    /// シェーダーにマテリアルパラメータを設定
    /// </summary>
    public void SetShaderUniforms(IShaderProgram shader)
    {
        shader.SetUniform("u_albedo", SourceMaterial.Albedo);
        shader.SetUniform("u_metallic", SourceMaterial.Metallic);
        shader.SetUniform("u_roughness", SourceMaterial.Roughness);
        shader.SetUniform("u_emission", SourceMaterial.Emission);

        // テクスチャスロットを設定
        shader.SetUniform("u_albedoTexture", 0);
        shader.SetUniform("u_normalTexture", 1);
        shader.SetUniform("u_metallicRoughnessTexture", 2);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        // デフォルトテクスチャ以外は解放
        if (AlbedoTexture != DefaultTextures.WhiteTexture)
            AlbedoTexture?.Dispose();

        if (NormalTexture != DefaultTextures.NormalTexture)
            NormalTexture?.Dispose();

        if (MetallicRoughnessTexture != DefaultTextures.DefaultMetallicRoughnessTexture)
            MetallicRoughnessTexture?.Dispose();

        _disposed = true;
    }
}
