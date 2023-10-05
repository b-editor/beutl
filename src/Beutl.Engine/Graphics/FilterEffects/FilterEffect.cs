using Beutl.Animation;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Graphics.Effects;

[DummyType(typeof(DummyFilterEffect))]
public abstract class FilterEffect : Animatable, IAffectsRender
{
    public static readonly CoreProperty<bool> IsEnabledProperty;
    private bool _isEnabled = true;

    static FilterEffect()
    {
        IsEnabledProperty = ConfigureProperty<bool, FilterEffect>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .Register();

        AffectsRender<FilterEffect>(IsEnabledProperty);
    }

    protected FilterEffect()
    {
        AnimationInvalidated += (_, e) => RaiseInvalidated(e);
    }

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetAndRaise(IsEnabledProperty, ref _isEnabled, value);
    }

    internal int Version { get; private set; }

    protected static void AffectsRender<T>(params CoreProperty[] properties)
        where T : FilterEffect
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
        RaiseInvalidated(e);
    }

    protected void RaiseInvalidated(RenderInvalidatedEventArgs args)
    {
        Invalidated?.Invoke(this, args);
        unchecked
        {
            Version++;
        }
    }

    // FilterEffectContext.Applyから呼び出される
    public abstract void ApplyTo(FilterEffectContext context);

    public virtual Rect TransformBounds(Rect bounds)
    {
        return bounds;
    }
}
