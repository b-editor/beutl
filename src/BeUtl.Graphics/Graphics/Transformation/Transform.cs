namespace BeUtl.Graphics.Transformation;

public abstract class Transform : ILogicalElement
{
    private bool _isEnabled = true;

    public abstract Matrix Value { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public Drawable? Parent { get; internal set; }

    ILogicalElement? ILogicalElement.LogicalParent => Parent;

    IEnumerable<ILogicalElement> ILogicalElement.LogicalChildren => Array.Empty<ILogicalElement>();

    event EventHandler<LogicalTreeAttachmentEventArgs> ILogicalElement.AttachedToLogicalTree
    {
        add => throw new NotSupportedException();
        remove => throw new NotSupportedException();
    }

    event EventHandler<LogicalTreeAttachmentEventArgs> ILogicalElement.DetachedFromLogicalTree
    {
        add => throw new NotSupportedException();
        remove => throw new NotSupportedException();
    }

    protected bool SetProperty<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            Parent?.InvalidateVisual();

            return true;
        }
        else
        {
            return false;
        }
    }

    void ILogicalElement.NotifyAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        Parent = e.Parent as Drawable;
    }

    void ILogicalElement.NotifyDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        Parent = null;
    }
}
