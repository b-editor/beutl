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

    public ShakeEffect()
    {
        ScanProperties<ShakeEffect>();
        UpdateOffset();
        // The shake follows the Id, so a new Id redraws it.
        Id.ValueChanged += (_, _) =>
        {
            UpdateOffset();
            RaiseEdited();
        };
    }

    [Display(Name = nameof(GraphicsStrings.ShakeEffect_StrengthX), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> StrengthX { get; } = Property.CreateAnimatable(50f);

    [Display(Name = nameof(GraphicsStrings.ShakeEffect_StrengthY), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> StrengthY { get; } = Property.CreateAnimatable(50f);

    [Display(Name = nameof(GraphicsStrings.Speed), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> Speed { get; } = Property.CreateAnimatable(100f);

    private void UpdateOffset()
    {
        int hash = Id.Value.GetHashCode();
        _offset = new Random(hash).NextSingle() * 1000 - 500;
    }

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
        bool versionBumped = true;
        resource.Reconcile(this, context, ref versionBumped);
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

        public override void Reconcile(EngineObject obj, CompositionContext context, ref bool versionBumped)
        {
            base.Reconcile(obj, context, ref versionBumped);

            ResourceReconciler.ReconcileValue(this, context, ((ShakeEffect)obj).StrengthX, ref _strengthX, ref versionBumped);
            ResourceReconciler.ReconcileValue(this, context, ((ShakeEffect)obj).StrengthY, ref _strengthY, ref versionBumped);
            ResourceReconciler.ReconcileValue(this, context, ((ShakeEffect)obj).Speed, ref _speed, ref versionBumped);

            float oldTime = _time;
            _time = (float)(context.Time - obj.TimeRange.Value.Start).ToSeconds();
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (!versionBumped && oldTime != _time)
            {
                Version++;
            }
        }

        protected override void ApplyTo(FilterEffectContext context)
        {
            var effect = (ShakeEffect)RequireOriginal();
            context.CustomEffect(
                (time: Time, speed: Speed, strengthX: StrengthX, strengthY: StrengthY, random: effect._random, offset: effect._offset),
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
                        randomX = ClampOffset(randomX);
                        randomY = ClampOffset(randomY);
                        target.Bounds = target.Bounds.Translate(new Vector(randomX, randomY));
                    });
                });
        }
    }
}
