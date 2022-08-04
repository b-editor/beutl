using BeUtl.Collections;

namespace BeUtl.Styling;

public class Style : IStyle
{
    private readonly Setters _setters;
    private Type _targetType = typeof(Styleable);

    public Style()
    {
        _setters = new(this);
        _setters.Invalidated += (_, _) => Invalidated?.Invoke(this, EventArgs.Empty);
    }

    public virtual Type TargetType
    {
        get => _targetType;
        set
        {
            if (_targetType != value)
            {
                _targetType = value;
                Invalidated?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public ICoreList<ISetter> Setters => _setters;

    ICoreReadOnlyList<ISetter> IStyle.Setters => _setters;

    public IStylingElement? StylingParent { get; private set; }

    public IEnumerable<IStylingElement> StylingChildren => _setters;

    public event EventHandler? Invalidated;

    public event EventHandler<StylingTreeAttachmentEventArgs>? AttachedToStylingTree;

    public event EventHandler<StylingTreeAttachmentEventArgs>? DetachedFromStylingTree;

    public IStyleInstance Instance(IStyleable target, IStyleInstance? baseStyle = null)
    {
        var array = new ISetterInstance[_setters.Count];
        int index = 0;
        foreach (ISetter item in _setters.GetMarshal().Value)
        {
            array[index++] = item.Instance(target);
        }

        return new StyleInstance(target, this, array, baseStyle);
    }

    void IStylingElement.NotifyAttachedToStylingTree(in StylingTreeAttachmentEventArgs e)
    {
        if (StylingParent is { })
            throw new StylingTreeException("This styling element already has a parent element.");

        StylingParent = e.Parent;
        AttachedToStylingTree?.Invoke(this, e);
    }

    void IStylingElement.NotifyDetachedFromStylingTree(in StylingTreeAttachmentEventArgs e)
    {
        if (!ReferenceEquals(e.Parent, StylingParent))
            throw new StylingTreeException("The detach source element and the parent element do not match.");

        StylingParent = null;
        DetachedFromStylingTree?.Invoke(this, e);
    }
}

public sealed class Style<T> : Style
    where T : Styleable
{
    public override Type TargetType
    {
        get => typeof(T);
        set => throw new InvalidOperationException();
    }
}
