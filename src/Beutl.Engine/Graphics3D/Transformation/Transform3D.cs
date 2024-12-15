using System.Numerics;
using Beutl.Animation;
using Beutl.Media;

namespace Beutl.Graphics3D.Transformation;

public abstract class Transform3D : Animatable, IMutableTransform3D
{
    public static readonly CoreProperty<bool> IsEnabledProperty;
    private bool _isEnabled = true;

    static Transform3D()
    {
        IsEnabledProperty = ConfigureProperty<bool, Transform3D>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .Register();

        AffectsRender<Transform3D>(IsEnabledProperty);
    }

    protected Transform3D()
    {
        AnimationInvalidated += (_, e) => RaiseInvalidated(e);
    }

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    public static ITransform3D Identity { get; } = new ImmutableTransform3D(Matrix4x4.Identity);

    public abstract Matrix4x4 Value { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetAndRaise(IsEnabledProperty, ref _isEnabled, value);
    }

    public ITransform3D ToImmutable()
    {
        return new ImmutableTransform3D(Value);
    }

    protected static void AffectsRender<T>(
        CoreProperty? property1 = null,
        CoreProperty? property2 = null,
        CoreProperty? property3 = null,
        CoreProperty? property4 = null)
        where T : Transform3D
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

    private void OnAffectsRenderInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        Invalidated?.Invoke(this, e);
    }

    protected static void AffectsRender<T>(params CoreProperty[] properties)
        where T : Transform3D
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

    protected void RaiseInvalidated(RenderInvalidatedEventArgs args)
    {
        Invalidated?.Invoke(this, args);
    }
}
