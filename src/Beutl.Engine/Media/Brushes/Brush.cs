using System.ComponentModel.DataAnnotations;

using Beutl.Animation;
using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Language;

namespace Beutl.Media;

/// <summary>
/// Describes how an area is painted.
/// </summary>
public abstract class Brush : Animatable, IMutableBrush
{
    public static readonly CoreProperty<float> OpacityProperty;
    public static readonly CoreProperty<ITransform?> TransformProperty;
    public static readonly CoreProperty<RelativePoint> TransformOriginProperty;
    private float _opacity = 100;
    private ITransform? _transform;
    private RelativePoint _transformOrigin;

    static Brush()
    {
        OpacityProperty = ConfigureProperty<float, Brush>(nameof(Opacity))
            .Accessor(o => o.Opacity, (o, v) => o.Opacity = v)
            .DefaultValue(100f)
            .Register();

        TransformProperty = ConfigureProperty<ITransform?, Brush>(nameof(Transform))
            .Accessor(o => o.Transform, (o, v) => o.Transform = v)
            .Register();

        TransformOriginProperty = ConfigureProperty<RelativePoint, Brush>(nameof(TransformOrigin))
            .Accessor(o => o.TransformOrigin, (o, v) => o.TransformOrigin = v)
            .Register();

        AffectsRender<Brush>(OpacityProperty, TransformProperty, TransformOriginProperty);
    }

    protected Brush()
    {
        AnimationInvalidated += (_, e) => RaiseInvalidated(e);
    }

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    /// <summary>
    /// Gets or sets the opacity of the brush.
    /// </summary>
    [Display(Name = nameof(Strings.Opacity), ResourceType = typeof(Strings))]
    [Range(0, 100)]
    public float Opacity
    {
        get => _opacity;
        set => SetAndRaise(OpacityProperty, ref _opacity, value);
    }

    /// <summary>
    /// Gets or sets the transform of the brush.
    /// </summary>
    [Display(Name = nameof(Strings.Transform), ResourceType = typeof(Strings))]
    public ITransform? Transform
    {
        get => _transform;
        set => SetAndRaise(TransformProperty, ref _transform, value);
    }

    /// <summary>
    /// Gets or sets the origin of the brush <see cref="Transform"/>
    /// </summary>
    [Display(Name = nameof(Strings.TransformOrigin), ResourceType = typeof(Strings))]
    public RelativePoint TransformOrigin
    {
        get => _transformOrigin;
        set => SetAndRaise(TransformOriginProperty, ref _transformOrigin, value);
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        (Transform as IAnimatable)?.ApplyAnimations(clock);
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
                s.RaiseInvalidated(new RenderInvalidatedEventArgs(s, e.Property.Name));

                if (e.OldValue is IAffectsRender oldAffectsRender)
                    oldAffectsRender.Invalidated -= s.OnAffectsRenderInvalidated;

                if (e.NewValue is IAffectsRender newAffectsRender)
                    newAffectsRender.Invalidated += s.OnAffectsRenderInvalidated;
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
                    s.RaiseInvalidated(new RenderInvalidatedEventArgs(s, e.Property.Name));

                    if (e.OldValue is IAffectsRender oldAffectsRender)
                        oldAffectsRender.Invalidated -= s.OnAffectsRenderInvalidated;

                    if (e.NewValue is IAffectsRender newAffectsRender)
                        newAffectsRender.Invalidated += s.OnAffectsRenderInvalidated;
                }
            });
        }
    }

    private void OnAffectsRenderInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        Invalidated?.Invoke(this, e);
    }

    protected void RaiseInvalidated(RenderInvalidatedEventArgs args)
    {
        Invalidated?.Invoke(this, args);
    }
}
