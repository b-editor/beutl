using Beutl.Configuration;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed class SceneRenderer : Renderer
{
    private readonly SceneCompositor _compositor;

    /// <inheritdoc cref="Renderer(int, int, RenderIntent, float, float, RenderCacheOptions)" path="/param[@name='intent']"/>
    public SceneRenderer(
        Scene scene,
        RenderIntent intent,
        float renderScale = 1f,
        bool disableResourceShare = false,
        float maxWorkingScale = float.PositiveInfinity,
        bool forceOriginalSource = false,
        EditorConfig? editorConfig = null,
        ProxyStoreConfig? proxyConfig = null)
        : base(scene.FrameSize.Value.Width, scene.FrameSize.Value.Height, intent, renderScale, maxWorkingScale,
            editorConfig == null ? RenderCacheOptions.Default : RenderCacheOptions.CreateFromConfiguration(editorConfig))
    {
        _compositor = new SceneCompositor(scene, editorConfig, proxyConfig)
        {
            DisableResourceShare = disableResourceShare,
            ForceOriginalSource = forceOriginalSource,
        };
    }

    public SceneCompositor Compositor => _compositor;

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        if (disposing)
            _compositor.Dispose();
    }
}
