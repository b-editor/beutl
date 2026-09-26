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

    /// <summary>Whether the content is being produced; the editor shows a progress ring.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>What the monitor is busy with, shown beside the progress ring.</summary>
    public string? BusyText { get; private set; }

    public void SetBusy(bool isBusy, string? text = null)
    {
        if (IsBusy == isBusy && BusyText == text)
            return;
        IsBusy = isBusy;
        BusyText = isBusy ? text : null;
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    public override Type? AssociatedType => null;

    public event EventHandler? ContentChanged;
}
