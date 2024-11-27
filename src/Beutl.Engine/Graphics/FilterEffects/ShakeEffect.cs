using Beutl.Animation;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

public class ShakeEffect : FilterEffect
{
    public static readonly CoreProperty<float> StrengthXProperty;
    public static readonly CoreProperty<float> StrengthYProperty;
    public static readonly CoreProperty<float> SpeedProperty;

    private readonly RenderInvalidatedEventArgs _invalidatedEventArgs;

    private readonly PerlinNoise _random = new();
    private float _time;
    private float _strengthX = 50;
    private float _strengthY = 50;
    private float _speed = 100;

    static ShakeEffect()
    {
        StrengthXProperty = ConfigureProperty<float, ShakeEffect>(nameof(StrengthX))
            .Accessor(o => o.StrengthX, (o, v) => o.StrengthX = v)
            .DefaultValue(50)
            .Register();

        StrengthYProperty = ConfigureProperty<float, ShakeEffect>(nameof(StrengthY))
            .Accessor(o => o.StrengthY, (o, v) => o.StrengthY = v)
            .DefaultValue(50)
            .Register();

        SpeedProperty = ConfigureProperty<float, ShakeEffect>(nameof(Speed))
            .Accessor(o => o.Speed, (o, v) => o.Speed = v)
            .DefaultValue(100)
            .Register();

        AffectsRender<ShakeEffect>(StrengthXProperty, StrengthYProperty, SpeedProperty);
    }

    public ShakeEffect()
    {
        _invalidatedEventArgs = new RenderInvalidatedEventArgs(this);
    }

    public float StrengthX
    {
        get => _strengthX;
        set => SetAndRaise(StrengthXProperty, ref _strengthX, value);
    }

    public float StrengthY
    {
        get => _strengthY;
        set => SetAndRaise(StrengthYProperty, ref _strengthY, value);
    }

    public float Speed
    {
        get => _speed;
        set => SetAndRaise(SpeedProperty, ref _speed, value);
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        _time = (float)clock.CurrentTime.TotalSeconds;

        RaiseInvalidated(_invalidatedEventArgs);
    }

    public override Rect TransformBounds(Rect bounds)
    {
        return Rect.Invalid;
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.CustomEffect((_time, _speed, _strengthX, _strengthY, _random), (data, effectContext) =>
        {
            effectContext.ForEach((i, t) =>
            {
                float randomX = data._random.Perlin(data._time * data._speed / 100, i);
                float randomY = data._random.Perlin(i, data._time * data._speed / 100);
                randomX = (randomX - 0.5F) * 2F * data._strengthX;
                randomY = (randomY - 0.5F) * 2F * data._strengthY;
                t.Bounds = t.Bounds.Translate(new Vector(randomX, randomY));
            });
        });
    }
}
