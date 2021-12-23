namespace BEditorNext.Animation;

public abstract class Animator : ILogicalElement
{
    public ILogicalElement? LogicalParent { get; private set; }

    public IEnumerable<ILogicalElement> LogicalChildren => Enumerable.Empty<ILogicalElement>();

    public event EventHandler<LogicalTreeAttachmentEventArgs>? AttachedToLogicalTree;
    public event EventHandler<LogicalTreeAttachmentEventArgs>? DetachedFromLogicalTree;

    void ILogicalElement.NotifyAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        LogicalParent = e.NewParent;
        AttachedToLogicalTree?.Invoke(this, e);
    }

    void ILogicalElement.NotifyDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        LogicalParent = e.NewParent;
        DetachedFromLogicalTree?.Invoke(this, e);
    }
}
