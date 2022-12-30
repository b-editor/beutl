using Beutl.Animation;
using Beutl.Rendering;

namespace Beutl.Operation;

public interface ISourceFilter : ISourceOperator
{
    SourceFilterScope Scope { get; }

    List<Renderable> Filter(IReadOnlyList<Renderable> renderables, IClock clock);
}

public enum SourceFilterScope
{
    Local,
    Global
}

public sealed class TestSourceFilter : SourceOperator, ISourceFilter
{
    public SourceFilterScope Scope => SourceFilterScope.Local;

    public void Filter(List<Renderable> renderables, IClock clock)
    {
        renderables.RemoveRange(0, renderables.Count - 1);
    }

    public List<Renderable> Filter(IReadOnlyList<Renderable> renderables, IClock clock)
    {
        if (renderables.Count == 0)
        {
            return new List<Renderable>(0);
        }
        else
        {
            var list = new List<Renderable>(1)
            {
                renderables[^1]
            };
            return list;
        }
    }
}
