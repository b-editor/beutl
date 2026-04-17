using Beutl.Graphics.Rendering;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed class SceneRenderer : Renderer
{
    private readonly SceneCompositor _compositor;

    public SceneRenderer(Scene scene, bool disableResourceShare = false)
        : base(scene.FrameSize.Width, scene.FrameSize.Height)
    {
        _compositor = new SceneCompositor(scene) { DisableResourceShare = disableResourceShare };
    }

    public SceneCompositor Compositor => _compositor;

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        if (disposing)
            _compositor.Dispose();
    }
}
