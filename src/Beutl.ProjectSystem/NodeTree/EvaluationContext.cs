using System.Runtime.CompilerServices;

using Beutl.Animation;
using Beutl.Rendering;

namespace Beutl.NodeTree;

public sealed class NodeEvaluationContext : EvaluationContext
{
    public NodeEvaluationContext(Node node, EvaluationContext context)
        : base(context)
    {
        Node = node;
    }

    public NodeEvaluationContext(Node node)
    {
        Node = node;
    }

    public Node Node { get; }

    public object? State { get; set; }

    public T GetOrSetStateWithFactory<T>(Func<T> factory)
    {
        if (State is T t)
        {
            return t;
        }

        T? value = factory();
        State = value;
        return value;
    }

    public T GetOrSetState<T>()
        where T : new()
    {
        if (State is T t)
        {
            return t;
        }

        T? value = new T();
        State = value;
        return value;
    }

    public T? GetOrDefaultState<T>()
    {
        if (State is T t)
        {
            return t;
        }

        return default;
    }
}

// Todo:
public class EvaluationContext
{
    internal IList<Renderable> _renderables;

    public EvaluationContext(EvaluationContext context)
    {
        Clock = context.Clock;
        List = context.List;
        _renderables = context._renderables;
        Id = context.Id;
    }

#pragma warning disable CS8618
    public EvaluationContext()
#pragma warning restore CS8618
    {
    }

    public IClock Clock { get; internal set; }

    public IReadOnlyList<NodeEvaluationContext> List { get; internal set; }

    public int Id { get; internal set; }

    public void AddRenderable(Renderable renderable)
    {
        _renderables.Add(renderable);
    }
}
