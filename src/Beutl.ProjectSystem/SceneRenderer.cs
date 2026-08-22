using Beutl.Graphics.Rendering;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed class SceneRenderer : Renderer
{
    private readonly SceneCompositor _compositor;

    /// <inheritdoc cref="Renderer(int, int, RenderIntent, float, float)" path="/param[@name='intent']"/>
    public SceneRenderer(
        Scene scene,
        RenderIntent intent,
        float renderScale = 1f,
        bool disableResourceShare = false,
        float maxWorkingScale = float.PositiveInfinity,
        bool forceOriginalSource = false)
        : base(scene.FrameSize.Width, scene.FrameSize.Height, intent, renderScale, maxWorkingScale)
    {
        _compositor = new SceneCompositor(scene)
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
