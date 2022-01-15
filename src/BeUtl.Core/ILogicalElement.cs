namespace BeUtl;

/// <summary>
/// Represents a element in the logical tree.
/// </summary>
public interface ILogicalElement
{
    /// <summary>
    /// Gets the logical parent.
    /// </summary>
    ILogicalElement? LogicalParent { get; }

    /// <summary>
    /// Gets the logical children.
    /// </summary>
    IEnumerable<ILogicalElement> LogicalChildren { get; }

    event EventHandler<LogicalTreeAttachmentEventArgs> AttachedToLogicalTree;
    event EventHandler<LogicalTreeAttachmentEventArgs> DetachedFromLogicalTree;

    void NotifyAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs e);

    void NotifyDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs e);
}
