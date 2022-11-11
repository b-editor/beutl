using System.Text.Json.Nodes;

using Beutl.Animation;

namespace Beutl.Styling;

public abstract class Styleable : Animatable, IStyleable
{
    public static readonly CoreProperty<Styles> StylesProperty;
    public static readonly CoreProperty<Styleable?> ParentProperty;
    private ILogicalElement? _parent;
    private readonly Styles _styles;
    private IStyleInstance? _styleInstance;

    static Styleable()
    {
        StylesProperty = ConfigureProperty<Styles, Styleable>(nameof(Styles))
            .Accessor(o => o.Styles, (o, v) => o.Styles = v)
            .Register();

        ParentProperty = ConfigureProperty<Styleable?, Styleable>(nameof(Parent))
            .Accessor(o => o.Parent, (o, v) => o.Parent = v)
            .Register();
    }

    protected Styleable()
    {
        _styles = new();
        _styles.Attached += item =>
        {
            item.Invalidated += Style_Invalidated;
        };
        _styles.Detached += item =>
        {
            item.Invalidated -= Style_Invalidated;
        };
        _styles.CollectionChanged += Style_Invalidated;
    }

    public event EventHandler<LogicalTreeAttachmentEventArgs>? AttachedToLogicalTree;

    public event EventHandler<LogicalTreeAttachmentEventArgs>? DetachedFromLogicalTree;

    private void Style_Invalidated(object? sender, EventArgs e)
    {
        _styleInstance = null;
    }

    public Styleable? Parent
    {
        get => _parent as Styleable;
        private set
        {
            Styleable? parent = Parent;
            SetAndRaise(ParentProperty, ref parent, value);
            _parent = parent;
        }
    }

    public Styles Styles
    {
        get => _styles;
        set
        {
            if (_styles != value)
            {
                _styles.Replace(value);
            }
        }
    }

    ILogicalElement? ILogicalElement.LogicalParent => _parent;

    IEnumerable<ILogicalElement> ILogicalElement.LogicalChildren => OnEnumerateChildren();

    public void InvalidateStyles()
    {
        if (_styleInstance != null)
        {
            _styleInstance.Dispose();
            _styleInstance = null;
        }
    }

    public virtual void ApplyStyling(IClock clock)
    {
        _styleInstance ??= Styles.Instance(this);

        if (_styleInstance != null)
        {
            _styleInstance.Begin();
            _styleInstance.Apply(clock);
            _styleInstance.End();
        }
    }

    IStyleInstance? IStyleable.GetStyleInstance(IStyle style)
    {
        IStyleInstance? styleInstance = _styleInstance;
        while (styleInstance != null)
        {
            if (styleInstance.Source == style)
            {
                return styleInstance;
            }
            else
            {
                styleInstance = styleInstance.BaseStyle;
            }
        }

        return null;
    }

    void IStyleable.StyleApplied(IStyleInstance instance)
    {
        _styleInstance = instance;
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject jobject)
        {
            if (jobject.TryGetPropertyValue("styles", out JsonNode? stylesNode)
                && stylesNode is JsonArray stylesArray)
            {
                Styles.Clear();
                Styles.EnsureCapacity(stylesArray.Count);

                foreach (JsonNode? styleNode in stylesArray)
                {
                    if (styleNode is JsonObject styleObject
                        && styleObject.ToStyle() is Style style)
                    {
                        Styles.Add(style);
                    }
                }
            }
        }
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);
        if (json is JsonObject jobject)
        {
            if (Styles.Count > 0)
            {
                var styles = new JsonArray();

                foreach (IStyle style in Styles.GetMarshal().Value)
                {
                    styles.Add(style.ToJson());
                }

                jobject["styles"] = styles;
            }
        }
    }

    protected static void LogicalChild<T>(
        CoreProperty? property1 = null,
        CoreProperty? property2 = null,
        CoreProperty? property3 = null,
        CoreProperty? property4 = null)
        where T : Styleable
    {
        static void onNext(CorePropertyChangedEventArgs e)
        {
            if (e.Sender is T s)
            {
                if (e.OldValue is ILogicalElement oldLogical)
                {
                    oldLogical.NotifyDetachedFromLogicalTree(new LogicalTreeAttachmentEventArgs(s));
                }

                if (e.NewValue is ILogicalElement newLogical)
                {
                    newLogical.NotifyAttachedToLogicalTree(new LogicalTreeAttachmentEventArgs(s));
                }
            }
        }

        property1?.Changed.Subscribe(onNext);
        property2?.Changed.Subscribe(onNext);
        property3?.Changed.Subscribe(onNext);
        property4?.Changed.Subscribe(onNext);
    }

    protected static void LogicalChild<T>(params CoreProperty[] properties)
        where T : Styleable
    {
        static void onNext(CorePropertyChangedEventArgs e)
        {
            if (e.Sender is T s)
            {
                if (e.OldValue is ILogicalElement oldLogical)
                {
                    oldLogical.NotifyDetachedFromLogicalTree(new LogicalTreeAttachmentEventArgs(s));
                }

                if (e.NewValue is ILogicalElement newLogical)
                {
                    newLogical.NotifyAttachedToLogicalTree(new LogicalTreeAttachmentEventArgs(s));
                }
            }
        }

        foreach (CoreProperty? item in properties)
        {
            item.Changed.Subscribe(onNext);
        }
    }

    protected virtual void OnAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
    }

    protected virtual void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
    }

    protected virtual IEnumerable<ILogicalElement> OnEnumerateChildren()
    {
        yield break;
    }

    void ILogicalElement.NotifyAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        if (_parent is { })
            throw new LogicalTreeException("This logical element already has a parent element.");

        OnAttachedToLogicalTree(e);
        _parent = e.Parent;
        AttachedToLogicalTree?.Invoke(this, e);
    }

    void ILogicalElement.NotifyDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        if (!ReferenceEquals(e.Parent, _parent))
            throw new LogicalTreeException("The detach source element and the parent element do not match.");

        OnDetachedFromLogicalTree(e);
        _parent = null;
        DetachedFromLogicalTree?.Invoke(this, e);
    }
}
