using System.Collections;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Collections;
using Beutl.Serialization;

namespace Beutl.Styling;

[ExcludeFromCodeCoverage]
public abstract class Styleable : Animatable, IStyleable
{
    public static readonly CoreProperty<Styles> StylesProperty;
    private readonly Styles _styles;
    private IStyleInstance? _styleInstance;

    static Styleable()
    {
        StylesProperty = ConfigureProperty<Styles, Styleable>(nameof(Styles))
            .Accessor(o => o.Styles, (o, v) => o.Styles = v)
            .Register();
    }

    protected Styleable()
    {
        _styles = [];
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

    private void Style_Invalidated(object? sender, EventArgs e)
    {
        _styleInstance = null;
    }

    [NotAutoSerialized]
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

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(Styles), Styles);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<Styles>(nameof(Styles)) is { } styles)
        {
            Styles = styles;
        }
    }
}
