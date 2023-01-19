using Beutl.Animation;
using Beutl.Rendering;

namespace Beutl.NodeTree;

// Todo:
public sealed class EvaluationContext
{
    private readonly IList<Renderable> _renderables;

    public EvaluationContext(IClock clock, IReadOnlyList<INode> list, IList<Renderable> renderables)
    {
        Clock = clock;
        List = list;
        _renderables = renderables;
    }

    public IClock Clock { get; }

    public IReadOnlyList<INode> List { get; }

    public void AddRenderable(Renderable renderable)
    {
        _renderables.Add(renderable);
    }
}
