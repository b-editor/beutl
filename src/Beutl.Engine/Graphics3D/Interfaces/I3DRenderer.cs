using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3Dレンダリングバックエンドの抽象化インターフェース
/// </summary>
public interface I3DRenderer : IDisposable
{
    /// <summary>
    /// レンダラーの名前（例: "OpenGL", "Vulkan", "Metal", "Direct3D"）
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 初期化
    /// </summary>
    bool Initialize();

    /// <summary>
    /// シャットダウン
    /// </summary>
    void Shutdown();

    /// <summary>
    /// フレーム開始
    /// </summary>
    void BeginFrame();

    /// <summary>
    /// フレーム終了
    /// </summary>
    void EndFrame();

    /// <summary>
    /// デフォルトのレンダーターゲットを作成
    /// </summary>
    I3DRenderTarget CreateRenderTarget(int width, int height, TextureFormat colorFormat, TextureFormat? depthFormat = null);

    /// <summary>
    /// メッシュリソースを作成
    /// </summary>
    I3DMeshResource CreateMesh(I3DMesh mesh);

    /// <summary>
    /// テクスチャリソースを作成
    /// </summary>
    ITextureResource CreateTexture(int width, int height, TextureFormat format, ReadOnlySpan<byte> data);

    /// <summary>
    /// マテリアルリソースを作成
    /// </summary>
    I3DMaterialResource CreateMaterial(I3DMaterial material);

    /// <summary>
    /// シェーダープログラムを作成
    /// </summary>
    IShaderProgram CreateShaderProgram(string vertexShader, string fragmentShader);

    /// <summary>
    /// 遅延レンダリングパイプラインを実行
    /// </summary>
    void RenderDeferred(I3DScene scene, I3DCamera camera, I3DRenderTarget target);

    /// <summary>
    /// フォワードレンダリングパイプラインを実行
    /// </summary>
    void RenderForward(I3DScene scene, I3DCamera camera, I3DRenderTarget target);
}
