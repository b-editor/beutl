using Beutl.Animation;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

public class ShakeEffect : FilterEffect
{
    public static readonly CoreProperty<float> StrengthXProperty;
    public static readonly CoreProperty<float> StrengthYProperty;
    public static readonly CoreProperty<float> SpeedProperty;

    private readonly RenderInvalidatedEventArgs _invalidatedEventArgs;

    // private readonly Random _random = new();
    private PerlinNoise _random = new();
    private float _randomX;
    private float _randomY;
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
        // _randomX = ((float)_random.NextDouble() - 0.5F) * 2F * _strengthX;
        // _randomY = ((float)_random.NextDouble() - 0.5F) * 2F * _strengthY;
        _randomX = _random.Perlin((float)clock.CurrentTime.TotalSeconds * _speed / 100, 0);
        _randomY = _random.Perlin(0, (float)clock.CurrentTime.TotalSeconds * _speed / 100);
        _randomX = (_randomX - 0.5F) * 2F * _strengthX;
        _randomY = (_randomY - 0.5F) * 2F * _strengthY;

        RaiseInvalidated(_invalidatedEventArgs);
    }

    public override Rect TransformBounds(Rect bounds)
    {
        return bounds.Translate(new Vector(_randomX, _randomY));
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.Transform(Matrix.CreateTranslation(_randomX, _randomY), BitmapInterpolationMode.Default);
    }
}
