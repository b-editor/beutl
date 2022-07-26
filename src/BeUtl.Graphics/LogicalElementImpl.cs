using BeUtl.Styling;

namespace BeUtl;

internal struct LogicalElementImpl : ILogicalElement
{
    public ILogicalElement? LogicalParent { get; set; }

    public IEnumerable<ILogicalElement> LogicalChildren => Enumerable.Empty<ILogicalElement>();

    public event EventHandler<LogicalTreeAttachmentEventArgs>? AttachedToLogicalTree;

    public event EventHandler<LogicalTreeAttachmentEventArgs>? DetachedFromLogicalTree;

    public void VerifyAttachedToLogicalTree()
    {
        if (LogicalParent is { })
            throw new LogicalTreeException("This logical element already has a parent element.");
    }

    public void VerifyDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        if (!ReferenceEquals(e.Parent, LogicalParent))
            throw new LogicalTreeException("The detach source element and the parent element do not match.");
    }

    public void NotifyAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        LogicalParent = e.Parent;
        AttachedToLogicalTree?.Invoke(this, e);
    }

    public void NotifyDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        LogicalParent = null;
        DetachedFromLogicalTree?.Invoke(this, e);
    }
}

internal struct StylingElementImpl : IStylingElement
{
    public IStylingElement? StylingParent { get; set; }

    public IEnumerable<IStylingElement> StylingChildren => Enumerable.Empty<IStylingElement>();

    public event EventHandler<StylingTreeAttachmentEventArgs>? AttachedToStylingTree;

    public event EventHandler<StylingTreeAttachmentEventArgs>? DetachedFromStylingTree;

    public void VerifyAttachedToStylingTree()
    {
        if (StylingParent is { })
            throw new StylingTreeException("This styling element already has a parent element.");
    }

    public void VerifyDetachedFromStylingTree(in StylingTreeAttachmentEventArgs e)
    {
        if (!ReferenceEquals(e.Parent, StylingParent))
            throw new StylingTreeException("The detach source element and the parent element do not match.");
    }

    public void NotifyAttachedToStylingTree(in StylingTreeAttachmentEventArgs e)
    {
        StylingParent = e.Parent;
        AttachedToStylingTree?.Invoke(this, e);
    }

    public void NotifyDetachedFromStylingTree(in StylingTreeAttachmentEventArgs e)
    {
        StylingParent = null;
        DetachedFromStylingTree?.Invoke(this, e);
    }
}
