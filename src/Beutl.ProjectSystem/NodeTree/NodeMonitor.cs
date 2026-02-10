namespace Beutl.NodeTree;

public class NodeMonitor<T> : NodeItem<T>, INodeMonitor
{
    private volatile bool _isEnabled = true;

    public NodeMonitorContentKind ContentKind { get; init; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value;
    }

    public new T? Value
    {
        get => base.Value;
        set
        {
            base.Value = value;
            ContentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public override Type? AssociatedType => null;

    public event EventHandler? ContentChanged;

    public override void PreEvaluate(EvaluationContext context)
    {
        // No-op: Property/Animation なし
    }
}
