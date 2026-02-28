using Beutl.Graphics.Rendering;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed class SceneRenderer(Scene scene) : Renderer(scene.FrameSize.Width, scene.FrameSize.Height)
{
    private readonly SceneCompositor _compositor = new(scene);

    public SceneCompositor Compositor => _compositor;

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        if (disposing)
            _compositor.Dispose();
    }
}
