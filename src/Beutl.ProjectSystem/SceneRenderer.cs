using Beutl.ProjectSystem;
using Beutl.Rendering;

namespace Beutl;

internal sealed class SceneRenderer : Renderer
{
    private readonly Scene _scene;

    public SceneRenderer(Scene scene, int width, int height)
        : base(width, height)
    {
        _scene = scene;
        GraphicsEvaluator = new SceneGraphicsEvaluator(_scene, this);
    }

    internal SceneGraphicsEvaluator GraphicsEvaluator { get; }

    protected override void RenderGraphicsCore()
    {
        GraphicsEvaluator.Evaluate();
        base.RenderGraphicsCore();
    }

    protected override void OnDispose(bool disposing)
    {
        base.OnDispose(disposing);
        GraphicsEvaluator.Dispose();
    }
}
