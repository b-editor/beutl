using Beutl.Graphics.Rendering;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed class SceneRenderer : Renderer
{
    private readonly SceneCompositor _compositor;

    public SceneRenderer(Scene scene, bool disableResourceShare = false, bool useProxyIfAvailable = false, float renderScale = 1.0f)
        : base(
            Math.Max(1, (int)Math.Round(scene.FrameSize.Width * renderScale)),
            Math.Max(1, (int)Math.Round(scene.FrameSize.Height * renderScale)))
    {
        _compositor = new SceneCompositor(scene)
        {
            DisableResourceShare = disableResourceShare,
            UseProxyIfAvailable = useProxyIfAvailable,
            RenderScale = renderScale,
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
