using Beutl.Animation;
using Beutl.Media;

namespace Beutl.Audio.Effects;

public abstract class SoundEffect : Animatable, IMutableSoundEffect
{
    public static readonly CoreProperty<bool> IsEnabledProperty;
    private bool _isEnabled = true;

    static SoundEffect()
    {
        IsEnabledProperty = ConfigureProperty<bool, SoundEffect>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .Register();

        AffectsRender<SoundEffect>(IsEnabledProperty);
    }

    protected SoundEffect()
    {
        AnimationInvalidated += (_, e) => RaiseInvalidated(e);
    }

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetAndRaise(IsEnabledProperty, ref _isEnabled, value);
    }

    public abstract ISoundProcessor CreateProcessor();

    public override void ApplyAnimations(IClock clock)
    {
        // SoundEffectはアニメーションに対応しない。
        //base.ApplyAnimations(clock);
    }

    protected static void AffectsRender<T>(params CoreProperty[] properties)
        where T : SoundEffect
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
