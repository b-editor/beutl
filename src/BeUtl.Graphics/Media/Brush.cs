using BeUtl.Styling;

namespace BeUtl.Media;

/// <summary>
/// Describes how an area is painted.
/// </summary>
public abstract class Brush : Styleable, IMutableBrush
{
    public static readonly CoreProperty<float> OpacityProperty;
    private float _opacity = 1;

    static Brush()
    {
        OpacityProperty = ConfigureProperty<float, Brush>(nameof(Opacity))
            .Accessor(o => o.Opacity, (o, v) => o.Opacity = v)
            .PropertyFlags(PropertyFlags.Styleable | PropertyFlags.Designable)
            .DefaultValue(1f)
            .Register();

        AffectsRender<Brush>(OpacityProperty);
    }

    public event EventHandler? Invalidated;

    /// <summary>
    /// Gets or sets the opacity of the brush.
    /// </summary>
    public float Opacity
    {
        get => _opacity;
        set => SetAndRaise(OpacityProperty, ref _opacity, value);
    }

    public abstract IBrush ToImmutable();

    protected static void AffectsRender<T>(
        CoreProperty? property1 = null,
        CoreProperty? property2 = null,
        CoreProperty? property3 = null,
        CoreProperty? property4 = null)
        where T : Brush
    {
        Action<CorePropertyChangedEventArgs> onNext = e =>
        {
            if (e.Sender is T s)
            {
                s.RaiseInvalidated();
            }
        };

        property1?.Changed.Subscribe(onNext);
        property2?.Changed.Subscribe(onNext);
        property3?.Changed.Subscribe(onNext);
        property4?.Changed.Subscribe(onNext);
    }

    protected static void AffectsRender<T>(params CoreProperty[] properties)
        where T : Brush
    {
        foreach (CoreProperty? item in properties)
        {
            item.Changed.Subscribe(e =>
            {
                if (e.Sender is T s)
                {
                    s.RaiseInvalidated();
                }
            });
        }
    }

    protected void RaiseInvalidated()
    {
        Invalidated?.Invoke(this, EventArgs.Empty);
    }
}
