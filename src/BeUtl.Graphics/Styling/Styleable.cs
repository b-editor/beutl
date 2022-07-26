using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using BeUtl.Animation;

namespace BeUtl.Styling;

public abstract class Styleable : Animatable, IStyleable
{
    public static readonly CoreProperty<Styles> StylesProperty;
    private readonly Styles _styles;
    private IStylingElement? _stylingParent;
    private IStyleInstance? _styleInstance;
    private EventHandler<StylingTreeAttachmentEventArgs>? _attachedToStylingTree;
    private EventHandler<StylingTreeAttachmentEventArgs>? _detachedFromStylingTree;

    static Styleable()
    {
        StylesProperty = ConfigureProperty<Styles, Styleable>(nameof(Styles))
            .Accessor(o => o.Styles, (o, v) => o.Styles = v)
            .Register();
    }

    protected Styleable()
    {
        _styles = new();
        _styles.Attached += item =>
        {
            item.Invalidated += Style_Invalidated;
            item.NotifyAttachedToStylingTree(new StylingTreeAttachmentEventArgs(this));
        };
        _styles.Detached += item =>
        {
            item.Invalidated -= Style_Invalidated;
            item.NotifyDetachedFromStylingTree(new StylingTreeAttachmentEventArgs(this));
        };
        _styles.CollectionChanged += Style_Invalidated;

        Animations.Attached += item =>
        {
            item.NotifyAttachedToStylingTree(new StylingTreeAttachmentEventArgs(this));
        };
        Animations.Detached += item =>
        {
            item.NotifyDetachedFromStylingTree(new StylingTreeAttachmentEventArgs(this));
        };
    }

    event EventHandler<StylingTreeAttachmentEventArgs> IStylingElement.AttachedToStylingTree
    {
        add => _attachedToStylingTree += value;
        remove => _attachedToStylingTree -= value;
    }

    event EventHandler<StylingTreeAttachmentEventArgs> IStylingElement.DetachedFromStylingTree
    {
        add => _detachedFromStylingTree += value;
        remove => _detachedFromStylingTree -= value;
    }

    private void Style_Invalidated(object? sender, EventArgs e)
    {
        _styleInstance = null;
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

    IStylingElement? IStylingElement.StylingParent => _stylingParent;

    IEnumerable<IStylingElement> IStylingElement.StylingChildren => _styles.Concat<IStylingElement>(Animations);

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
        if (_styleInstance == null)
        {
            _styleInstance = Styles.Instance(this);
        }

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
                if (Styles.Capacity < stylesArray.Count)
                {
                    Styles.Capacity = stylesArray.Count;
                }

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

                foreach (IStyle style in Styles.AsSpan())
                {
                    styles.Add(style.ToJson());
                }

                jobject["styles"] = styles;
            }
        }
    }

    protected virtual void OnAttachedToStylingTree(in StylingTreeAttachmentEventArgs e)
    {
    }
    
    protected virtual void OnDetachedFromStylingTree(in StylingTreeAttachmentEventArgs e)
    {
    }

    void IStylingElement.NotifyAttachedToStylingTree(in StylingTreeAttachmentEventArgs e)
    {
        if (_stylingParent is { })
            throw new StylingTreeException("This styling element already has a parent element.");

        OnAttachedToStylingTree(e);

        _stylingParent = e.Parent;
        _attachedToStylingTree?.Invoke(this, e);
    }

    void IStylingElement.NotifyDetachedFromStylingTree(in StylingTreeAttachmentEventArgs e)
    {
        if (!ReferenceEquals(e.Parent, _stylingParent))
            throw new StylingTreeException("The detach source element and the parent element do not match.");

        OnDetachedFromStylingTree(e);

        _stylingParent = null;
        _detachedFromStylingTree?.Invoke(this, e);
    }
}
