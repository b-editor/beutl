using Beutl.Animation;
using Beutl.Rendering;

namespace Beutl.NodeTree;

// Todo:
public class EvaluationContext
{
    internal IList<Renderable> _renderables;

    public EvaluationContext(EvaluationContext context)
    {
        Clock = context.Clock;
        Renderer = context.Renderer;
        List = context.List;
        _renderables = context._renderables;
    }

#pragma warning disable CS8618
    public EvaluationContext()
#pragma warning restore CS8618
    {
    }

    public IClock Clock { get; internal set; }

    public IRenderer Renderer { get; internal set; }

    public IReadOnlyList<NodeEvaluationContext> List { get; internal set; }

    public void AddRenderable(Renderable renderable)
    {
        _renderables.Add(renderable);
    }
}
