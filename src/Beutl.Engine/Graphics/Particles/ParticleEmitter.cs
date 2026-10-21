using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Particles;

[Display(Name = nameof(GraphicsStrings.ParticleEmitter), ResourceType = typeof(GraphicsStrings))]
[SuppressResourceClassGeneration]
public partial class ParticleEmitter : Drawable
{
    public ParticleEmitter()
    {
        ScanProperties<ParticleEmitter>();
    }

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_Seed), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_EmitterGroup))]
    public IExpressionProperty<int> Seed { get; } = Property.CreateAnimatable(0);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_EmitterShape), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_EmitterGroup))]
    public IExpressionProperty<EmitterShape> EmitterShape { get; } = Property.Create(Particles.EmitterShape.Point);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_EmitterWidth), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_EmitterGroup))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> EmitterWidth { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_EmitterHeight), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_EmitterGroup))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> EmitterHeight { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_MaxParticles), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_EmitterGroup))]
    [Range(1, 50000)]
    public IExpressionProperty<int> MaxParticles { get; } = Property.Create(5000);

    /// <summary>
    /// Time to simulate before the emitter starts, from zero to one minute (3,600 fixed steps).
    /// Values outside this range fall back to zero, disabling prewarming. Only particle simulation
    /// advances, not property animations.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_PrewarmDuration), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_EmissionGroup))]
    [Range(typeof(TimeSpan), "00:00:00", "00:01:00", ParseLimitsInInvariantCulture = true)]
    public IExpressionProperty<TimeSpan> PrewarmDuration { get; } = Property.Create<TimeSpan>();

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_EmissionRate), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_EmissionGroup))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> EmissionRate { get; } = Property.CreateAnimatable(60f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_Lifetime), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_EmissionGroup))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Lifetime { get; } = Property.CreateAnimatable(2f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_LifetimeRandom), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_EmissionGroup))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> LifetimeRandom { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.Speed), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_VelocityGroup))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Speed { get; } = Property.CreateAnimatable(200f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_SpeedRandom), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_VelocityGroup))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> SpeedRandom { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.Direction), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_VelocityGroup))]
    public IExpressionProperty<float> Direction { get; } = Property.CreateAnimatable(-90f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_Spread), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_VelocityGroup))]
    [Range(0, 360)]
    public IExpressionProperty<float> Spread { get; } = Property.CreateAnimatable(30f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_Gravity), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_PhysicsGroup))]
    public IExpressionProperty<float> Gravity { get; } = Property.CreateAnimatable(200f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_AirResistance), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_PhysicsGroup))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> AirResistance { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_TurbulenceStrength), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_PhysicsGroup))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> TurbulenceStrength { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_TurbulenceScale), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_PhysicsGroup))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> TurbulenceScale { get; } = Property.CreateAnimatable(0.01f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_TurbulenceSpeed), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_PhysicsGroup))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> TurbulenceSpeed { get; } = Property.CreateAnimatable(1f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_ParticleDrawable), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_VisualGroup))]
    public IExpressionProperty<Drawable?> ParticleDrawable { get; } = Property.Create<Drawable?>();

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_ParticleSize), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_VisualGroup))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> ParticleSize { get; } = Property.CreateAnimatable(10f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_SizeRandom), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_VisualGroup))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> SizeRandom { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.Color), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_VisualGroup))]
    public IExpressionProperty<Color> ParticleColor { get; } = Property.CreateAnimatable(Colors.White);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_ParticleOpacity), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_VisualGroup))]
    [Range(0, 100)]
    public IExpressionProperty<float> ParticleOpacity { get; } = Property.CreateAnimatable(100f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_InitialRotation), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.Rotation))]
    public IExpressionProperty<float> InitialRotation { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_InitialRotationRandom), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.Rotation))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> InitialRotationRandom { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_AngularVelocity), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.Rotation))]
    public IExpressionProperty<float> AngularVelocity { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_EndSizeMultiplier), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_OverLifeGroup))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> EndSizeMultiplier { get; } = Property.CreateAnimatable(1f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_EndOpacityMultiplier), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_OverLifeGroup))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> EndOpacityMultiplier { get; } = Property.CreateAnimatable(0f);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_EndColor), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_OverLifeGroup))]
    public IExpressionProperty<Color> EndColor { get; } = Property.CreateAnimatable(Colors.White);

    [Display(Name = nameof(GraphicsStrings.ParticleEmitter_UseEndColor), ResourceType = typeof(GraphicsStrings),
        GroupName = nameof(GraphicsStrings.ParticleEmitter_OverLifeGroup))]
    public IExpressionProperty<bool> UseEndColor { get; } = Property.Create(false);

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
        bool versionBumped = true;
        resource.Reconcile(this, context, ref versionBumped);
        return resource;
    }

    public new class Resource : Drawable.Resource
    {
        private readonly ParticleSimulator _simulator = new();

        private ParticleSettings _settings;
        private Drawable.Resource? _particleDrawable;
        private double _time;

        public Drawable.Resource? ParticleDrawable => _particleDrawable;

        internal ReadOnlyMemory<Particle> GetAliveParticles()
        {
            return _simulator.GetAliveParticles();
        }

        internal static ParticleSettings CreateSettings(ParticleEmitter emitter, CompositionContext context)
        {
            return new ParticleSettings(
                context.Get(emitter.Seed),
                context.Get(emitter.EmitterShape),
                context.Get(emitter.EmitterWidth),
                context.Get(emitter.EmitterHeight),
                context.Get(emitter.MaxParticles),
                context.Get(emitter.PrewarmDuration),
                context.Get(emitter.EmissionRate),
                context.Get(emitter.Lifetime),
                context.Get(emitter.LifetimeRandom),
                context.Get(emitter.Speed),
                context.Get(emitter.SpeedRandom),
                context.Get(emitter.Direction),
                context.Get(emitter.Spread),
                context.Get(emitter.Gravity),
                context.Get(emitter.AirResistance),
                context.Get(emitter.TurbulenceStrength),
                context.Get(emitter.TurbulenceScale),
                context.Get(emitter.TurbulenceSpeed),
                context.Get(emitter.ParticleSize),
                context.Get(emitter.SizeRandom),
                context.Get(emitter.ParticleColor),
                context.Get(emitter.ParticleOpacity),
                context.Get(emitter.InitialRotation),
                context.Get(emitter.InitialRotationRandom),
                context.Get(emitter.AngularVelocity),
                context.Get(emitter.EndSizeMultiplier),
                context.Get(emitter.EndOpacityMultiplier),
                context.Get(emitter.EndColor),
                context.Get(emitter.UseEndColor));
        }

        public override void Reconcile(EngineObject obj, CompositionContext context, ref bool versionBumped)
        {
            base.Reconcile(obj, context, ref versionBumped);

            var emitter = (ParticleEmitter)obj;

            var settings = CreateSettings(emitter, context);
            // Base properties may have already bumped the render version. Track simulation changes separately.
            bool parametersChanged = !settings.Equals(_settings);
            _settings = settings;

            if (parametersChanged)
            {
                BumpVersion(ref versionBumped);
                _simulator.InvalidateCache();
            }

            ResourceReconciler.ReconcileChild(this, context, emitter.ParticleDrawable, ref _particleDrawable, ref versionBumped);

            double oldTime = _time;
            _time = (context.Time - obj.TimeRange.Start).TotalSeconds;
            if (_time >= 0)
            {
                // Keep the original composition clock for animated properties and the particle drawable.
                _time += Math.Max(0, _settings.PrewarmDuration.TotalSeconds);
            }

            _simulator.Simulate(_time, in _settings);

            // The particles move whenever the simulated time does, unless this reconcile already bumped the version.
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (!versionBumped && oldTime != _time)
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
