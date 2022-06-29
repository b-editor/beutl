using BeUtl.Animation;

namespace BeUtl.Styling;

public abstract class Styleable : Element, IStyleable
{
    public static readonly CoreProperty<Styles> StylesProperty;
    private readonly Styles _styles;
    private IStyleInstance? _styleInstance;

    static Styleable()
    {
        StylesProperty = ConfigureProperty<Styles, Styleable>(nameof(Styles))
            .SerializeName("styles")
            .Accessor(o => o.Styles, (o, v) => o.Styles = v)
            .Register();
    }

    protected Styleable()
    {
        _styles = new()
        {
            Attached = item => item.Invalidated += Style_Invalidated,
            Detached = item => item.Invalidated -= Style_Invalidated
        };
        _styles.CollectionChanged += Style_Invalidated;
    }

    private void Style_Invalidated(object? sender, EventArgs e)
    {
        InvalidateStyles();
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

    public void InvalidateStyles()
    {
        if (_styleInstance != null)
        {
            _styleInstance.Dispose();
            _styleInstance = null;
        }
    }

    public void ApplyStyling(IClock clock)
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
        }

        return null;
    }

    void IStyleable.StyleApplied(IStyleInstance instance)
    {
        _styleInstance = instance;
    }
}
