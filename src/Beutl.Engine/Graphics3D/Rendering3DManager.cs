using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3Dレンダリングマネージャー（Beutlシステムとの統合）
/// </summary>
public class Rendering3DManager : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<Rendering3DManager>();
    
    private static Rendering3DManager? s_instance;
    private I3DRenderer? _currentRenderer;
    private DeferredRenderPipeline? _deferredPipeline;
    private bool _disposed;

    public static Rendering3DManager Instance => s_instance ??= new Rendering3DManager();

    public I3DRenderer? CurrentRenderer => _currentRenderer;
    public bool IsInitialized => _currentRenderer != null;
    public string? CurrentBackendName { get; private set; }

    private Rendering3DManager()
    {
    }

    /// <summary>
    /// 3Dレンダリングシステムを初期化
    /// </summary>
    public bool Initialize(string? preferredBackend = null)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Rendering3DManager));

        try
        {
            // 既存のレンダラーがあれば破棄
            Shutdown();

            // レンダラーを作成
            var factory = Renderer3DFactory.Instance;
            
            if (!string.IsNullOrEmpty(preferredBackend) && factory.IsBackendAvailable(preferredBackend))
            {
                _currentRenderer = factory.CreateRenderer(preferredBackend);
                CurrentBackendName = preferredBackend;
            }
            else
            {
                _currentRenderer = factory.CreateBestAvailableRenderer();
                CurrentBackendName = _currentRenderer?.Name;
            }

            if (_currentRenderer == null)
            {
                s_logger.LogError("Failed to create any 3D renderer");
                return false;
            }

            // 遅延レンダリングパイプラインを作成
            _deferredPipeline = new DeferredRenderPipeline(_currentRenderer);

            s_logger.LogInformation("3D rendering system initialized with {Backend} backend", CurrentBackendName);
            return true;
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Failed to initialize 3D rendering system");
            return false;
        }
    }

    /// <summary>
    /// 3Dレンダリングシステムをシャットダウン
    /// </summary>
    public void Shutdown()
    {
        try
        {
            _deferredPipeline?.Dispose();
            _deferredPipeline = null;

            _currentRenderer?.Shutdown();
            _currentRenderer?.Dispose();
            _currentRenderer = null;

            CurrentBackendName = null;
            s_logger.LogInformation("3D rendering system shut down");
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Error during 3D rendering system shutdown");
        }
    }

    /// <summary>
    /// レンダーターゲットを作成
    /// </summary>
    public I3DRenderTarget? CreateRenderTarget(int width, int height, TextureFormat colorFormat = TextureFormat.Rgba8, TextureFormat? depthFormat = null)
    {
        if (_currentRenderer == null)
        {
            s_logger.LogWarning("Cannot create render target: 3D renderer not initialized");
            return null;
        }

        return _currentRenderer.CreateRenderTarget(width, height, colorFormat, depthFormat);
    }

    /// <summary>
    /// 3Dシーンをレンダリング
    /// </summary>
    public void RenderScene(I3DScene scene, I3DCamera camera, I3DRenderTarget target, bool useDeferred = true)
    {
        if (_currentRenderer == null)
        {
            s_logger.LogWarning("Cannot render scene: 3D renderer not initialized");
            return;
        }

        try
        {
            _currentRenderer.BeginFrame();

            if (useDeferred && _deferredPipeline != null)
            {
                // 遅延レンダリングを使用
                var context = new DeferredRenderContext
                {
                    Scene = scene,
                    Camera = camera
                };
                
                _deferredPipeline.SetupRenderTargets(target.Width, target.Height);
                _deferredPipeline.Render(context);
            }
            else
            {
                // フォワードレンダリングを使用
                _currentRenderer.RenderForward(scene, camera, target);
            }

            _currentRenderer.EndFrame();
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Error during scene rendering");
        }
    }

    /// <summary>
    /// レンダリング統計情報を取得
    /// </summary>
    public RenderingStatistics GetStatistics()
    {
        return new RenderingStatistics
        {
            BackendName = CurrentBackendName ?? "None",
            IsInitialized = IsInitialized,
            FrameCount = 0, // 実装で更新
            DrawCalls = 0,  // 実装で更新
            Triangles = 0   // 実装で更新
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Shutdown();
        _disposed = true;
    }
}
