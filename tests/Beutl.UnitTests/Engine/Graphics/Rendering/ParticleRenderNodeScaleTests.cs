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

    private static (ParticleEmitter.Resource Resource, CountingParticleDrawable Drawable)
        BuildResourceWithCountingParticleDrawable()
    {
        var particle = new CountingParticleDrawable();
        var emitter = new ParticleEmitter();
        emitter.ParticleDrawable.CurrentValue = particle;

        var ctx = new CompositionContext(TimeSpan.FromSeconds(1.0));
        return ((ParticleEmitter.Resource)emitter.ToResource(ctx), particle);
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
    public void Process_EmitsOpTaggedAtOutputScale_ConcreteNotUnbounded(float outputScale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ParticleEmitter.Resource resource = BuildResourceWithAliveParticles();
            Assert.That(resource.GetAliveParticles().Length, Is.GreaterThanOrEqualTo(1),
                "precondition: at least one alive particle is required for Process to emit an op");

            using var node = new ParticleRenderNode(resource);
            var context = new RenderNodeContext([], RenderIntent.Delivery, outputScale: outputScale);

            RenderNodeOperation[] ops = node.Process(context);

            Assert.That(ops, Is.Not.Empty, "ParticleRenderNode emitted no op despite alive particles");
            Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(outputScale).Within(1e-4),
                $"the particle composite was not tagged At(w) with w == s_out ({outputScale})");
            Assert.That(ops[0].EffectiveScale.IsUnbounded, Is.False,
                "the particle composite was over-reported as re-rasterizable vector (Unbounded)");

            DisposeAll(ops);
        });
    }

    [Test]
    public void Process_AuxiliaryScaleDoesNotReplaceFrameParticleCache()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var (resource, drawable) = BuildResourceWithCountingParticleDrawable();
            using (resource)
            using (var node = new ParticleRenderNode(resource))
            {
                DisposeAll(node.Process(new RenderNodeContext(
                    [], RenderIntent.Preview, outputScale: 1f)));
                DisposeAll(node.Process(new RenderNodeContext(
                    [], RenderIntent.Preview, outputScale: 2f,
                    pullPurpose: RenderPullPurpose.Auxiliary)));
                DisposeAll(node.Process(new RenderNodeContext(
                    [], RenderIntent.Preview, outputScale: 1f)));

                Assert.That(drawable.RenderCount, Is.EqualTo(2),
                    "the frame cache should survive an auxiliary pull at a different scale");
            }
        });
    }

    [Test]
    public void Process_WhenParticleDrawableBufferClamps_TagsActualDensity()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using ParticleEmitter.Resource resource = BuildResourceWithLargeParticleDrawable();
            Assert.That(resource.GetAliveParticles().Length, Is.GreaterThanOrEqualTo(1),
                "precondition: at least one alive particle is required for Process to emit an op");

            using var node = new ParticleRenderNode(resource);
            var context = new RenderNodeContext([], RenderIntent.Delivery, outputScale: 8f);

            RenderNodeOperation[] ops = node.Process(context);

            Assert.That(ops, Is.Not.Empty, "ParticleRenderNode emitted no op despite alive particles");
            Assert.That(ops[0].EffectiveScale.IsUnbounded, Is.False);
            Assert.That(ops[0].EffectiveScale.Value, Is.LessThan(8f),
                "the particle op must report the clamped buffer density, not the nominal output scale");
            Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(
                RenderNodeContext.ClampWorkingScaleToBufferBudget(new Rect(0, 0, 4000, 10), 8f)).Within(1e-3f));

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

internal sealed partial class CountingParticleDrawable : Drawable
{
    public int RenderCount { get; private set; }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(20, 20);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        RenderCount++;
        context.DrawRectangle(new Rect(0, 0, 20, 20), Brushes.Resource.White, null);
    }
}
