using BeUtl.Styling;

namespace BeUtl.Animation;

public abstract class BaseAnimation : ILogicalElement, IStylingElement
{
    private LogicalElementImpl _logicalElement;
    private StylingElementImpl _stylingElement;

    protected BaseAnimation(CoreProperty property)
    {
        Property = property;
    }

    public CoreProperty Property { get; }

    public ILogicalElement? LogicalParent => _logicalElement.LogicalParent;

    public IEnumerable<ILogicalElement> LogicalChildren => Enumerable.Empty<ILogicalElement>();

    public IStylingElement? StylingParent => _stylingElement.StylingParent;

    public IEnumerable<IStylingElement> StylingChildren => Enumerable.Empty<IStylingElement>();

    public event EventHandler<LogicalTreeAttachmentEventArgs> AttachedToLogicalTree
    {
        add => _logicalElement.AttachedToLogicalTree += value;
        remove => _logicalElement.AttachedToLogicalTree -= value;
    }

    public event EventHandler<LogicalTreeAttachmentEventArgs> DetachedFromLogicalTree
    {
        add => _logicalElement.DetachedFromLogicalTree += value;
        remove => _logicalElement.DetachedFromLogicalTree -= value;
    }

    public event EventHandler<StylingTreeAttachmentEventArgs> AttachedToStylingTree
    {
        add => _stylingElement.AttachedToStylingTree += value;
        remove => _stylingElement.AttachedToStylingTree -= value;
    }

    public event EventHandler<StylingTreeAttachmentEventArgs> DetachedFromStylingTree
    {
        add => _stylingElement.DetachedFromStylingTree += value;
        remove => _stylingElement.DetachedFromStylingTree -= value;
    }

    protected virtual void OnAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
    }

    protected virtual void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
    }
    
    protected virtual void OnAttachedToStylingTree(in StylingTreeAttachmentEventArgs args)
    {
    }

    protected virtual void OnDetachedFromStylingTree(in StylingTreeAttachmentEventArgs args)
    {
    }

    void ILogicalElement.NotifyAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        _logicalElement.VerifyAttachedToLogicalTree();
        OnAttachedToLogicalTree(e);
        _logicalElement.NotifyAttachedToLogicalTree(e);
    }

    void ILogicalElement.NotifyDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        _logicalElement.VerifyDetachedFromLogicalTree(e);
        OnDetachedFromLogicalTree(e);
        _logicalElement.NotifyDetachedFromLogicalTree(e);
    }

    void IStylingElement.NotifyAttachedToStylingTree(in StylingTreeAttachmentEventArgs e)
    {
        _stylingElement.VerifyAttachedToStylingTree();
        OnAttachedToStylingTree(e);
        _stylingElement.NotifyAttachedToStylingTree(e);
    }

    void IStylingElement.NotifyDetachedFromStylingTree(in StylingTreeAttachmentEventArgs e)
    {
        _stylingElement.VerifyDetachedFromStylingTree(e);
        OnDetachedFromStylingTree(e);
        _stylingElement.NotifyDetachedFromStylingTree(e);
    }
}
