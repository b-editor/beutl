using Beutl.Animation;
using Beutl.Media;

namespace Beutl.Audio.Effects;

public abstract class AudioEffect : Animatable, IMutableAudioEffect
{
    public static readonly CoreProperty<bool> IsEnabledProperty;
    private bool _isEnabled = true;

    static AudioEffect()
    {
        IsEnabledProperty = ConfigureProperty<bool, AudioEffect>(nameof(IsEnabled))
            .Accessor(o => o.IsEnabled, (o, v) => o.IsEnabled = v)
            .DefaultValue(true)
            .Register();

        AffectsRender<AudioEffect>(IsEnabledProperty);
    }

    protected AudioEffect()
    {
        AnimationInvalidated += (_, e) => RaiseInvalidated(e);
    }

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetAndRaise(IsEnabledProperty, ref _isEnabled, value);
    }

    public abstract IAudioEffectProcessor CreateProcessor();

    protected static void AffectsRender<T>(params CoreProperty[] properties)
        where T : AudioEffect
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
