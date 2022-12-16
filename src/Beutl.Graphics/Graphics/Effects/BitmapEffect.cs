using Beutl.Animation;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

public abstract class BitmapEffect : Animatable, IMutableBitmapEffect
{
    public static readonly CoreProperty<bool> IsEnabledProperty;
    private bool _isEnabled = true;

    static BitmapEffect()
    {
        IsEnabledProperty = ConfigureProperty<bool, BitmapEffect>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .SerializeName("is-enabled")
            .Register();

        AffectsRender<BitmapEffect>(IsEnabledProperty);
    }

    protected BitmapEffect()
    {
        AnimationInvalidated += (_, e) => RaiseInvalidated(e);
    }

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    public abstract IBitmapProcessor Processor { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetAndRaise(IsEnabledProperty, ref _isEnabled, value);
    }

    public virtual Rect TransformBounds(Rect rect)
    {
        return rect;
    }

    protected static void AffectsRender<T>(params CoreProperty[] properties)
        where T : BitmapEffect
    {
        foreach (CoreProperty? item in properties)
        {
            item.Changed.Subscribe(e =>
            {
                if (e.Sender is T s)
                {
                    s.RaiseInvalidated(new RenderInvalidatedEventArgs(s, e.Property.Name));
                }
            });
        }
    }

    protected void RaiseInvalidated(RenderInvalidatedEventArgs args)
    {
        Invalidated?.Invoke(this, args);
    }
}
