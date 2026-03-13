using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Particles;

[Display(Name = nameof(GraphicsStrings.ParticleEmitter), ResourceType = typeof(GraphicsStrings))]
[SuppressResourceClassGeneration]
public class ParticleEmitter : Drawable
{
    public ParticleEmitter()
    {
        ScanProperties<ParticleEmitter>();
    }

    // Emitter
    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_Seed), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_EmitterGroup))]
    public IProperty<int> Seed { get; } = Property.CreateAnimatable(0);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_EmitterShape), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_EmitterGroup))]
    public IProperty<EmitterShape> EmitterShape { get; } = Property.Create(Particles.EmitterShape.Point);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_EmitterWidth), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_EmitterGroup))]
    [Range(0, float.MaxValue)]
    public IProperty<float> EmitterWidth { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_EmitterHeight), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_EmitterGroup))]
    [Range(0, float.MaxValue)]
    public IProperty<float> EmitterHeight { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_MaxParticles), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_EmitterGroup))]
    [Range(1, 50000)]
    public IProperty<int> MaxParticles { get; } = Property.Create(5000);

    // Emission
    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_EmissionRate), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_EmissionGroup))]
    [Range(0, float.MaxValue)]
    public IProperty<float> EmissionRate { get; } = Property.CreateAnimatable(60f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_Lifetime), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_EmissionGroup))]
    [Range(0, float.MaxValue)]
    public IProperty<float> Lifetime { get; } = Property.CreateAnimatable(2f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_LifetimeRandom), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_EmissionGroup))]
    [Range(0, float.MaxValue)]
    public IProperty<float> LifetimeRandom { get; } = Property.CreateAnimatable(0f);

    // Velocity
    [Display(Name = nameof(GraphicsStrings.Speed), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_VelocityGroup))]
    [Range(0, float.MaxValue)]
    public IProperty<float> Speed { get; } = Property.CreateAnimatable(200f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_SpeedRandom), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_VelocityGroup))]
    [Range(0, float.MaxValue)]
    public IProperty<float> SpeedRandom { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.Direction), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_VelocityGroup))]
    public IProperty<float> Direction { get; } = Property.CreateAnimatable(-90f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_Spread), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_VelocityGroup))]
    [Range(0, 360)]
    public IProperty<float> Spread { get; } = Property.CreateAnimatable(30f);

    // Physics
    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_Gravity), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_PhysicsGroup))]
    public IProperty<float> Gravity { get; } = Property.CreateAnimatable(200f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_AirResistance), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_PhysicsGroup))]
    [Range(0, float.MaxValue)]
    public IProperty<float> AirResistance { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_TurbulenceStrength), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_PhysicsGroup))]
    [Range(0, float.MaxValue)]
    public IProperty<float> TurbulenceStrength { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_TurbulenceScale), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_PhysicsGroup))]
    [Range(0, float.MaxValue)]
    public IProperty<float> TurbulenceScale { get; } = Property.CreateAnimatable(0.01f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_TurbulenceSpeed), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_PhysicsGroup))]
    [Range(0, float.MaxValue)]
    public IProperty<float> TurbulenceSpeed { get; } = Property.CreateAnimatable(1f);

    // Visual
    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_ParticleDrawable), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_VisualGroup))]
    public IProperty<Drawable?> ParticleDrawable { get; } = Property.Create<Drawable?>();

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_ParticleSize), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_VisualGroup))]
    [Range(0, float.MaxValue)]
    public IProperty<float> ParticleSize { get; } = Property.CreateAnimatable(10f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_SizeRandom), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_VisualGroup))]
    [Range(0, float.MaxValue)]
    public IProperty<float> SizeRandom { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.Color), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_VisualGroup))]
    public IProperty<Color> ParticleColor { get; } = Property.CreateAnimatable(Colors.White);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_ParticleOpacity), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_VisualGroup))]
    [Range(0, 100)]
    public IProperty<float> ParticleOpacity { get; } = Property.CreateAnimatable(100f);

    // Rotation
    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_InitialRotation), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.Rotation))]
    public IProperty<float> InitialRotation { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_InitialRotationRandom), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.Rotation))]
    [Range(0, float.MaxValue)]
    public IProperty<float> InitialRotationRandom { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_AngularVelocity), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.Rotation))]
    public IProperty<float> AngularVelocity { get; } = Property.CreateAnimatable(0f);

    // Over Life
    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_EndSizeMultiplier), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_OverLifeGroup))]
    [Range(0, float.MaxValue)]
    public IProperty<float> EndSizeMultiplier { get; } = Property.CreateAnimatable(1f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_EndOpacityMultiplier), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_OverLifeGroup))]
    [Range(0, float.MaxValue)]
    public IProperty<float> EndOpacityMultiplier { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_EndColor), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_OverLifeGroup))]
    public IProperty<Color> EndColor { get; } = Property.CreateAnimatable(Colors.White);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_UseEndColor), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_OverLifeGroup))]
    public IProperty<bool> UseEndColor { get; } = Property.Create(false);

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        return default;
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        context.DrawNode(
            r,
            p => new ParticleRenderNode(p),
            (node, p) => node.Update(p));
    }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new class Resource : Drawable.Resource
    {
        private readonly ParticleSimulator _simulator = new();

        private int _seed;
        private EmitterShape _emitterShape;
        private float _emitterWidth;
        private float _emitterHeight;
        private int _maxParticles;
        private float _emissionRate;
        private float _lifetime;
        private float _lifetimeRandom;
        private float _speed;
        private float _speedRandom;
        private float _direction;
        private float _spread;
        private float _gravity;
        private float _airResistance;
        private float _turbulenceStrength;
        private float _turbulenceScale;
        private float _turbulenceSpeed;
        private Drawable.Resource? _particleDrawable;
        private float _particleSize;
        private float _sizeRandom;
        private Color _color;
        private float _particleOpacity;
        private float _initialRotation;
        private float _initialRotationRandom;
        private float _angularVelocity;
        private float _endSizeMultiplier;
        private float _endOpacityMultiplier;
        private Color _endColor;
        private bool _useEndColor;
        private float _time;

        public Drawable.Resource? ParticleDrawable => _particleDrawable;

        internal ReadOnlyMemory<Particle> GetAliveParticles()
        {
            return _simulator.GetAliveParticles();
        }

        public override void Update(EngineObject obj, CompositionContext context, ref bool updateOnly)
        {
            base.Update(obj, context, ref updateOnly);

            var emitter = (ParticleEmitter)obj;

            var versionBefore = Version;
            CompareAndUpdate(context, emitter.Seed, ref _seed, ref updateOnly);
            CompareAndUpdate(context, emitter.EmitterShape, ref _emitterShape, ref updateOnly);
            CompareAndUpdate(context, emitter.EmitterWidth, ref _emitterWidth, ref updateOnly);
            CompareAndUpdate(context, emitter.EmitterHeight, ref _emitterHeight, ref updateOnly);
            CompareAndUpdate(context, emitter.MaxParticles, ref _maxParticles, ref updateOnly);
            CompareAndUpdate(context, emitter.EmissionRate, ref _emissionRate, ref updateOnly);
            CompareAndUpdate(context, emitter.Lifetime, ref _lifetime, ref updateOnly);
            CompareAndUpdate(context, emitter.LifetimeRandom, ref _lifetimeRandom, ref updateOnly);
            CompareAndUpdate(context, emitter.Speed, ref _speed, ref updateOnly);
            CompareAndUpdate(context, emitter.SpeedRandom, ref _speedRandom, ref updateOnly);
            CompareAndUpdate(context, emitter.Direction, ref _direction, ref updateOnly);
            CompareAndUpdate(context, emitter.Spread, ref _spread, ref updateOnly);
            CompareAndUpdate(context, emitter.Gravity, ref _gravity, ref updateOnly);
            CompareAndUpdate(context, emitter.AirResistance, ref _airResistance, ref updateOnly);
            CompareAndUpdate(context, emitter.TurbulenceStrength, ref _turbulenceStrength, ref updateOnly);
            CompareAndUpdate(context, emitter.TurbulenceScale, ref _turbulenceScale, ref updateOnly);
            CompareAndUpdate(context, emitter.TurbulenceSpeed, ref _turbulenceSpeed, ref updateOnly);
            CompareAndUpdate(context, emitter.ParticleSize, ref _particleSize, ref updateOnly);
            CompareAndUpdate(context, emitter.SizeRandom, ref _sizeRandom, ref updateOnly);
            CompareAndUpdate(context, emitter.ParticleColor, ref _color, ref updateOnly);
            CompareAndUpdate(context, emitter.ParticleOpacity, ref _particleOpacity, ref updateOnly);
            CompareAndUpdate(context, emitter.InitialRotation, ref _initialRotation, ref updateOnly);
            CompareAndUpdate(context, emitter.InitialRotationRandom, ref _initialRotationRandom, ref updateOnly);
            CompareAndUpdate(context, emitter.AngularVelocity, ref _angularVelocity, ref updateOnly);
            CompareAndUpdate(context, emitter.EndSizeMultiplier, ref _endSizeMultiplier, ref updateOnly);
            CompareAndUpdate(context, emitter.EndOpacityMultiplier, ref _endOpacityMultiplier, ref updateOnly);
            CompareAndUpdate(context, emitter.EndColor, ref _endColor, ref updateOnly);
            CompareAndUpdate(context, emitter.UseEndColor, ref _useEndColor, ref updateOnly);
            var paramChanged = versionBefore != Version;

            // Update particle drawable resource
            CompareAndUpdateObject(context, emitter.ParticleDrawable, ref _particleDrawable, ref updateOnly);

            // Time tracking
            float oldTime = _time;
            _time = (float)(context.Time - obj.TimeRange.Start).TotalSeconds;

            if (paramChanged)
            {
                _simulator.InvalidateCache();
            }

            // Run simulation
            _simulator.Simulate(
                _time,
                _seed,
                _emitterShape,
                _emitterWidth,
                _emitterHeight,
                _maxParticles,
                _emissionRate,
                _lifetime,
                _lifetimeRandom,
                _speed,
                _speedRandom,
                _direction,
                _spread,
                _gravity,
                _airResistance,
                _turbulenceStrength,
                _turbulenceScale,
                _turbulenceSpeed,
                _particleSize,
                _sizeRandom,
                _color,
                _particleOpacity,
                _initialRotation,
                _initialRotationRandom,
                _angularVelocity,
                _endSizeMultiplier,
                _endOpacityMultiplier,
                _endColor,
                _useEndColor);

            // Always increment version since particles move every frame
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (!updateOnly && oldTime != _time)
            {
                Version++;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _particleDrawable?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
