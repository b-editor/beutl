using BeUtl.Animation;
using BeUtl.Graphics;
using BeUtl.Graphics.Transformation;
using BeUtl.Styling;

namespace BeUtl.Media;

/// <summary>
/// Describes how an area is painted.
/// </summary>
public abstract class Brush : Animatable, IMutableBrush
{
    public static readonly CoreProperty<float> OpacityProperty;
    public static readonly CoreProperty<ITransform?> TransformProperty;
    public static readonly CoreProperty<RelativePoint> TransformOriginProperty;
    private float _opacity = 1;
    private ITransform? _transform;
    private RelativePoint _transformOrigin;

    static Brush()
    {
        OpacityProperty = ConfigureProperty<float, Brush>(nameof(Opacity))
            .Accessor(o => o.Opacity, (o, v) => o.Opacity = v)
            .PropertyFlags(PropertyFlags.All)
            .DefaultValue(1f)
            .SerializeName("opacity")
            .Register();

        TransformProperty = ConfigureProperty<ITransform?, Brush>(nameof(Transform))
            .Accessor(o => o.Transform, (o, v) => o.Transform = v)
            .PropertyFlags(PropertyFlags.All & ~PropertyFlags.Animatable)
            .SerializeName("transform")
            .Register();

        TransformOriginProperty = ConfigureProperty<RelativePoint, Brush>(nameof(TransformOrigin))
            .Accessor(o => o.TransformOrigin, (o, v) => o.TransformOrigin = v)
            .PropertyFlags(PropertyFlags.All)
            .SerializeName("transform-origin")
            .Register();

        AffectsRender<Brush>(OpacityProperty, TransformProperty, TransformOriginProperty);
    }

    protected Brush()
    {
        AnimationInvalidated += (_, _) => RaiseInvalidated();
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

    /// <summary>
    /// Gets or sets the transform of the brush.
    /// </summary>
    public ITransform? Transform
    {
        get => _transform;
        set => SetAndRaise(TransformProperty, ref _transform, value);
    }

    /// <summary>
    /// Gets or sets the origin of the brush <see cref="Transform"/>
    /// </summary>
    public RelativePoint TransformOrigin
    {
        get => _transformOrigin;
        set => SetAndRaise(TransformOriginProperty, ref _transformOrigin, value);
    }

    public abstract IBrush ToImmutable();

    protected static void AffectsRender<T>(
        CoreProperty? property1 = null,
        CoreProperty? property2 = null,
        CoreProperty? property3 = null,
        CoreProperty? property4 = null)
        where T : Brush
    {
        static void onNext(CorePropertyChangedEventArgs e)
        {
            if (e.Sender is T s)
            {
                s.RaiseInvalidated();
            }
        }

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
