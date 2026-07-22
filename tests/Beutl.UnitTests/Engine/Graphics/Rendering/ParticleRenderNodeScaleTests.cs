using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Particles;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// ParticleRenderNode rasterizes at s_out and tags its op At(w), never Unbounded.
[NonParallelizable]
[TestFixture]
public class ParticleRenderNodeScaleTests
{
    // With the ParticleEmitter defaults (EmissionRate=60/s, Lifetime=2s, MaxParticles=5000) and
    // TimeRange.Start at zero, the simulation deterministically holds alive particles at t=1s, so
    // Process emits an op.
    private static ParticleEmitter.Resource BuildResourceWithAliveParticles()
    {
        var emitter = new ParticleEmitter();
        var ctx = new CompositionContext(TimeSpan.FromSeconds(1.0));
        return (ParticleEmitter.Resource)emitter.ToResource(ctx);
    }

    private static ParticleEmitter.Resource BuildResourceWithLargeParticleDrawable()
    {
        var particle = new RectShape();
        particle.Width.CurrentValue = 4000;
        particle.Height.CurrentValue = 10;
        particle.Fill.CurrentValue = Brushes.White;

        var emitter = new ParticleEmitter();
        emitter.ParticleDrawable.CurrentValue = particle;

        var ctx = new CompositionContext(TimeSpan.FromSeconds(1.0));
        return (ParticleEmitter.Resource)emitter.ToResource(ctx);
    }

    [Test]
    public void Resource_AfterOneSecond_HasAliveParticles()
    {
        ParticleEmitter.Resource resource = BuildResourceWithAliveParticles();
        Assert.That(resource.GetAliveParticles().Length, Is.GreaterThanOrEqualTo(1),
            "the particle simulation produced no alive particles at t=1s with default emission");
    }

    // Particle composite reports At(w) concretely, even at w == 1.
    [TestCase(1.0f)]
    [TestCase(2.0f)]
    public void MaterializedOutput_IsTaggedAtOutputScale_ConcreteNotUnbounded(float outputScale)
    {
        using ParticleEmitter.Resource resource = BuildResourceWithAliveParticles();
        Assert.That(resource.GetAliveParticles().Length, Is.GreaterThanOrEqualTo(1),
            "precondition: at least one alive particle is required for recording to emit a fragment");

        using var pipeline = ScaleRecordingTestHelper.Pipeline(
            new ParticleRenderNode(resource),
            ScaleRecordingTestHelper.Materialize());
        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.Measure(pipeline, outputScale);

        Assert.That(measurement.HasFragments, Is.True,
            "ParticleRenderNode emitted no fragment despite alive particles");
        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(outputScale).Within(1e-4),
            $"the materialized particle composite was not tagged At(w) with w == s_out ({outputScale})");
        Assert.That(measurement.EffectiveScale.IsUnbounded, Is.False,
            "a materialized particle composite was reported as re-rasterizable vector (Unbounded)");
    }

    [Test]
    public void MaterializedOutput_WhenParticleDrawableBufferClamps_TagsActualDensity()
    {
        using ParticleEmitter.Resource resource = BuildResourceWithLargeParticleDrawable();
        Assert.That(resource.GetAliveParticles().Length, Is.GreaterThanOrEqualTo(1),
            "precondition: at least one alive particle is required for recording to emit a fragment");

        using var pipeline = ScaleRecordingTestHelper.Pipeline(
            new ParticleRenderNode(resource),
            ScaleRecordingTestHelper.Materialize());
        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.Measure(pipeline, outputScale: 8);

        Assert.That(measurement.HasFragments, Is.True,
            "ParticleRenderNode emitted no fragment despite alive particles");
        Assert.That(measurement.EffectiveScale.IsUnbounded, Is.False);
        Assert.That(measurement.EffectiveScale.Value, Is.LessThan(8),
            "the materialized particle output must report the clamped buffer density, not the nominal output scale");
        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(
            RenderScaleUtilities.ClampWorkingScaleToBufferBudget(measurement.OutputBounds, 8)).Within(1e-3));
    }
}
