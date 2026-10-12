using Beutl.Media;

namespace Beutl.Graphics.Particles;

/// <summary>The emitter inputs a simulation depends on; a change to any of them invalidates its checkpoints.</summary>
internal readonly record struct ParticleSettings(
    int Seed,
    EmitterShape EmitterShape,
    float EmitterWidth,
    float EmitterHeight,
    int MaxParticles,
    TimeSpan PrewarmDuration,
    float EmissionRate,
    float Lifetime,
    float LifetimeRandom,
    float Speed,
    float SpeedRandom,
    float Direction,
    float Spread,
    float Gravity,
    float AirResistance,
    float TurbulenceStrength,
    float TurbulenceScale,
    float TurbulenceSpeed,
    float ParticleSize,
    float SizeRandom,
    Color ParticleColor,
    float ParticleOpacity,
    float InitialRotation,
    float InitialRotationRandom,
    float AngularVelocity,
    float EndSizeMultiplier,
    float EndOpacityMultiplier,
    Color EndColor,
    bool UseEndColor);
