using Beutl.Graphics.Rendering.OpenGL;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3Dレンダラーファクトリー
/// </summary>
public class Renderer3DFactory : I3DRendererFactory
{
    private static readonly ILogger s_logger = Log.CreateLogger<Renderer3DFactory>();

    private static Renderer3DFactory? s_instance;
    private readonly Dictionary<string, Func<I3DRenderer?>> _rendererCreators = [];

    public static Renderer3DFactory Instance => s_instance ??= new Renderer3DFactory();

    private Renderer3DFactory()
    {
        RegisterDefaultRenderers();
    }

    public IReadOnlyList<string> SupportedBackends => _rendererCreators.Keys.ToList();

    private void RegisterDefaultRenderers()
    {
        // OpenGLレンダラーを登録
        _rendererCreators["OpenGL"] = () =>
        {
            try
            {
                var renderer = new OpenGLRenderer();
                return renderer.Initialize() ? renderer : null;
            }
            catch (Exception ex)
            {
                s_logger.LogError(ex, "Failed to create OpenGL renderer");
                return null;
            }
        };

        // 将来のレンダラーの登録場所
        // _rendererCreators["Vulkan"] = () => new VulkanRenderer();
        // _rendererCreators["Metal"] = () => new MetalRenderer();
        // _rendererCreators["Direct3D"] = () => new Direct3DRenderer();
    }

    public bool IsBackendAvailable(string backendName)
    {
        if (!_rendererCreators.ContainsKey(backendName))
            return false;

        try
        {
            // 実際にレンダラーを作成してテスト
            using var renderer = _rendererCreators[backendName]();
            return renderer != null;
        }
        catch (Exception ex)
        {
            s_logger.LogDebug(ex, "Backend {BackendName} is not available", backendName);
            return false;
        }
    }

    public I3DRenderer? CreateRenderer(string backendName)
    {
        if (!_rendererCreators.TryGetValue(backendName, out var creator))
        {
            s_logger.LogWarning("Unknown renderer backend: {BackendName}", backendName);
            return null;
        }

        try
        {
            var renderer = creator();
            if (renderer != null)
            {
                s_logger.LogInformation("Created {BackendName} renderer successfully", backendName);
            }
            else
            {
                s_logger.LogWarning("Failed to create {BackendName} renderer", backendName);
            }
            return renderer;
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Exception while creating {BackendName} renderer", backendName);
            return null;
        }
    }

    /// <summary>
    /// 利用可能な最適なレンダラーを自動選択して作成
    /// </summary>
    public I3DRenderer? CreateBestAvailableRenderer()
    {
        // 優先順位順でレンダラーを試行
        string[] preferredOrder = ["OpenGL", "Vulkan", "Metal", "Direct3D"];

        foreach (string backend in preferredOrder)
        {
            if (IsBackendAvailable(backend))
            {
                var renderer = CreateRenderer(backend);
                if (renderer != null)
                {
                    s_logger.LogInformation("Selected {BackendName} as the best available renderer", backend);
                    return renderer;
                }
            }
        }

        s_logger.LogError("No suitable 3D renderer backend available");
        return null;
    }

    /// <summary>
    /// カスタムレンダラーを登録
    /// </summary>
    public void RegisterRenderer(string name, Func<I3DRenderer?> creator)
    {
        _rendererCreators[name] = creator;
        s_logger.LogInformation("Registered custom renderer: {Name}", name);
    }

    /// <summary>
    /// レンダラーの登録を解除
    /// </summary>
    public void UnregisterRenderer(string name)
    {
        if (_rendererCreators.Remove(name))
        {
            s_logger.LogInformation("Unregistered renderer: {Name}", name);
        }
    }
}
