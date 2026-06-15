using Beutl.Composition;
using Beutl.Graphics.Particles;
using Beutl.Graphics.Rendering;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// feature 003 (FR-029): particles have no concrete bitmap input, so the supply-driven working density is just
// the output scale s_out. ParticleRenderNode rasterizes the per-particle drawable into a ceil(bounds × w) buffer
// and tags its composite op At(w). These tests assert the emitted op carries a CONCRETE At(w) density (never
// re-rasterizable Unbounded), at output scales 1.0 and 2.0, so a parent boundary treats the composite as the
// bitmap it is (FR-019b) instead of re-rasterizing above the pixels it has.
[NonParallelizable]
[TestFixture]
public class ParticleRenderNodeScaleTests
{
    // With the ParticleEmitter defaults (EmissionRate=60/s, Lifetime=2s, MaxParticles=5000) and TimeRange.Start
    // at TimeSpan.Zero, context.Time is the elapsed simulation time. At t=1s the simulation deterministically
    // holds alive particles, so Process emits an op.
    private static ParticleEmitter.Resource BuildResourceWithAliveParticles()
    {
        var emitter = new ParticleEmitter();
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

    // The particle composite is bitmap content at the density its per-particle drawable was rasterized at
    // (FR-019b): w == s_out, reported concretely (At(w).IsUnbounded == false), even at w == 1.
    [TestCase(1.0f)]
    [TestCase(2.0f)]
    public void Process_EmitsOpTaggedAtOutputScale_ConcreteNotUnbounded(float outputScale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ParticleEmitter.Resource resource = BuildResourceWithAliveParticles();
            Assert.That(resource.GetAliveParticles().Length, Is.GreaterThanOrEqualTo(1),
                "precondition: at least one alive particle is required for Process to emit an op");

            using var node = new ParticleRenderNode(resource);
            var context = new RenderNodeContext([], outputScale: outputScale);

            RenderNodeOperation[] ops = node.Process(context);

            Assert.That(ops, Is.Not.Empty, "ParticleRenderNode emitted no op despite alive particles");
            Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(outputScale).Within(1e-4),
                $"the particle composite was not tagged At(w) with w == s_out ({outputScale})");
            Assert.That(ops[0].EffectiveScale.IsUnbounded, Is.False,
                "the particle composite was over-reported as re-rasterizable vector (Unbounded)");

            DisposeAll(ops);
        });
    }

    private static void DisposeAll(RenderNodeOperation[] ops)
    {
        foreach (RenderNodeOperation op in ops)
        {
            op.Dispose();
        }
    }
}
