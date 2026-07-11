using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.ShakeEffect), ResourceType = typeof(GraphicsStrings))]
[SuppressResourceClassGeneration]
public partial class ShakeEffect : FilterEffect
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

    [Display(Name = nameof(GraphicsStrings.ShakeEffect_StrengthX), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> StrengthX { get; } = Property.CreateAnimatable(50f);

    [Display(Name = nameof(GraphicsStrings.ShakeEffect_StrengthY), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> StrengthY { get; } = Property.CreateAnimatable(50f);

    [Display(Name = nameof(GraphicsStrings.Speed), ResourceType = typeof(GraphicsStrings))]
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

    public override void Describe(EffectGraphBuilder builder, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        // One shake vector per describe, shared by every branch of a downstream split. The legacy pipeline varied the
        // offset per intra-effect target index; that per-branch variance is an accepted difference (maintainer
        // decision) — post-split branches shake identically here.
        float a = r.Time * r.Speed / 100 + _offset;
        float b = _offset;
        float randomX = ClampOffset((_random.Perlin(a, b) - 0.5F) * 2F * r.StrengthX);
        float randomY = ClampOffset((_random.Perlin(b, a) - 0.5F) * 2F * r.StrengthY);

        var translate = new Vector(randomX, randomY);
        // Forward translates by `translate`; producing output region `rect` samples the input over `rect − translate`,
        // so backward must translate by the inverse. An identity backward crops an upstream pass to the un-translated
        // region and drops the translated source band (A3).
        builder.Geometry(GeometryNodeDescriptor.Create(
            session => TransformGeometry.Render(session, Matrix.CreateTranslation(translate)),
            BoundsContract.Create(rect => rect.Translate(translate), rect => rect.Translate(-translate)),
            structuralToken: nameof(ShakeEffect)));
    }

    // A non-finite or runaway translation would produce an unallocatable geometry bounds downstream; clamp it so
    // a degenerate strength/speed animation cannot crash the executor.
    private static float ClampOffset(float value)
    {
        const float maxOffset = 100_000f;
        if (!float.IsFinite(value))
        {
            return 0;
        }

        return Math.Clamp(value, -maxOffset, maxOffset);
    }

    public override Resource ToResource(CompositionContext context)
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

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
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
