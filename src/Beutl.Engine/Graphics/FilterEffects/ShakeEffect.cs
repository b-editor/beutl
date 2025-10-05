using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

public partial class ShakeEffect : FilterEffect
{
    private readonly RenderInvalidatedEventArgs _invalidatedEventArgs;
    private readonly PerlinNoise _random = new();
    private float _time;
    private float _offset;

    static ShakeEffect()
    {
        AffectsRender<ShakeEffect>(IdProperty);
    }

    public ShakeEffect()
    {
        ScanProperties<ShakeEffect>();
        _invalidatedEventArgs = new RenderInvalidatedEventArgs(this);
    }

    [Display(Name = nameof(Strings.StrengthX), ResourceType = typeof(Strings))]
    public IProperty<float> StrengthX { get; } = Property.CreateAnimatable(50f);

    [Display(Name = nameof(Strings.StrengthY), ResourceType = typeof(Strings))]
    public IProperty<float> StrengthY { get; } = Property.CreateAnimatable(50f);

    [Display(Name = nameof(Strings.Speed), ResourceType = typeof(Strings))]
    public IProperty<float> Speed { get; } = Property.CreateAnimatable(100f);

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        _time = (float)clock.CurrentTime.TotalSeconds;

        RaiseInvalidated(_invalidatedEventArgs);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args is CorePropertyChangedEventArgs<Guid> targs)
        {
            if (targs.Property.Id == IdProperty.Id)
            {
                int hash = Id.GetHashCode();
                _offset = new Random(hash).NextSingle() * 1000 - 500;
            }
        }
    }

    public override Rect TransformBounds(Rect bounds)
    {
        return Rect.Invalid;
    }

    public override void ApplyTo(FilterEffectContext context)
    {
        context.CustomEffect(
            (time: _time, speed: Speed.CurrentValue, strengthX: StrengthX.CurrentValue, strengthY: StrengthY.CurrentValue, random: _random, offset: _offset),
            static (data, effectContext) =>
            {
                effectContext.ForEach((i, target) =>
                {
                    float a = data.time * data.speed / 100 + data.offset;
                    float b = i + data.offset;
                    float randomX = data.random.Perlin(a, b);
                    float randomY = data.random.Perlin(b, a);
                    randomX = (randomX - 0.5F) * 2F * data.strengthX;
                    randomY = (randomY - 0.5F) * 2F * data.strengthY;
                    target.Bounds = target.Bounds.Translate(new Vector(randomX, randomY));
                });
            });
    }
}
