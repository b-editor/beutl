using Beutl.Graphics.Rendering;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed class SceneRenderer : Renderer
{
    private readonly SceneCompositor _compositor;

    public SceneRenderer(
        Scene scene,
        float renderScale = 1f,
        bool disableResourceShare = false,
        float maxWorkingScale = float.PositiveInfinity,
        bool forceOriginalSource = false,
        RenderIntent intent = RenderIntent.Preview)
        : base(scene.FrameSize.Width, scene.FrameSize.Height, renderScale, maxWorkingScale, intent)
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
