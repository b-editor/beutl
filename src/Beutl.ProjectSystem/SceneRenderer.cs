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
        SceneRenderServices services,
        RenderIntent intent,
        float renderScale = 1f,
        bool disableResourceShare = false,
        float maxWorkingScale = float.PositiveInfinity,
        bool forceOriginalSource = false)
        : base(scene.FrameSize.Value.Width, scene.FrameSize.Value.Height, intent, renderScale, maxWorkingScale,
            (services ?? throw new ArgumentNullException(nameof(services))).EditorConfig is { } editorConfig
                ? RenderCacheOptions.CreateFromConfiguration(editorConfig)
                : RenderCacheOptions.Default)
    {
        _compositor = new SceneCompositor(scene, services)
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
