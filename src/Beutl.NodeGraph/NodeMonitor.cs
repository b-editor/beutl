namespace Beutl.NodeGraph;

public class NodeMonitor<T> : NodeMember<T>, INodeMonitor
{
    public NodeMonitorContentKind ContentKind { get; init; }

    public T? Value
    {
        get;
        set
        {
            field = value;
            ContentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public override Type? AssociatedType => null;

    public event EventHandler? ContentChanged;
}
