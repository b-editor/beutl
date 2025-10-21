using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

[SuppressResourceClassGeneration]
public class ShakeEffect : FilterEffect
{
    private readonly PerlinNoise _random = new();
    private float _offset;

    static ShakeEffect()
    {
        AffectsRender<ShakeEffect>(IdProperty);
    }

    public ShakeEffect()
    {
        ScanProperties<ShakeEffect>();
    }

    [Display(Name = nameof(Strings.StrengthX), ResourceType = typeof(Strings))]
    public IProperty<float> StrengthX { get; } = Property.CreateAnimatable(50f);

    [Display(Name = nameof(Strings.StrengthY), ResourceType = typeof(Strings))]
    public IProperty<float> StrengthY { get; } = Property.CreateAnimatable(50f);

    [Display(Name = nameof(Strings.Speed), ResourceType = typeof(Strings))]
    public IProperty<float> Speed { get; } = Property.CreateAnimatable(100f);

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

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        context.CustomEffect(
            (time: r.Time, speed: r.Speed, strengthX: r.StrengthX, strengthY: r.StrengthY, random: _random, offset: _offset),
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

    public override Resource ToResource(RenderContext context)
    {
        var resource = new Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new class Resource : FilterEffect.Resource
    {
        private float _strengthX;
        private float _strengthY;
        private float _speed;
        private float _time;

        public float StrengthX => _strengthX;

        public float StrengthY => _strengthY;

        public float Speed => _speed;

        public float Time => _time;

        public override void Update(EngineObject obj, RenderContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);

            CompareAndUpdate(context, ((ShakeEffect)obj).StrengthX, ref _strengthX, ref updateOnly);
            CompareAndUpdate(context, ((ShakeEffect)obj).StrengthY, ref _strengthY, ref updateOnly);
            CompareAndUpdate(context, ((ShakeEffect)obj).Speed, ref _speed, ref updateOnly);

            float oldTime = _time;
            _time = (float)(context.Time - obj.TimeRange.Start).TotalSeconds;
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (!updateOnly && oldTime != _time)
            {
                Version++;
            }
        }
    }
}
