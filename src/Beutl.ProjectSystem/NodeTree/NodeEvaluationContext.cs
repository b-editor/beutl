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

        T? value = new();
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
