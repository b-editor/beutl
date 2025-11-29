using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics.Rendering;

namespace Beutl;

public class EvaluationContext
{
    internal IList<EngineObject> _renderables;

    public EvaluationContext(EvaluationContext context)
    {
        Renderer = context.Renderer;
        List = context.List;
        _renderables = context._renderables;
        Target = context.Target;
    }

#pragma warning disable CS8618
    public EvaluationContext()
#pragma warning restore CS8618
    {
    }

    public IRenderer Renderer { get; internal set; }

    public IReadOnlyList<EvaluationContext> List { get; internal set; }

    public EvaluationTarget Target { get; internal set; }

    public void AddRenderable(EngineObject renderable)
    {
        _renderables?.Add(renderable);
    }
}
