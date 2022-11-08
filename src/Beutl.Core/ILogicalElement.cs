namespace Beutl;

public interface ILogicalElement
{
    ILogicalElement? LogicalParent { get; }

    IEnumerable<ILogicalElement> LogicalChildren { get; }

    event EventHandler<LogicalTreeAttachmentEventArgs> AttachedToLogicalTree;
    event EventHandler<LogicalTreeAttachmentEventArgs> DetachedFromLogicalTree;

    void NotifyAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs e);

    void NotifyDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs e);
}
