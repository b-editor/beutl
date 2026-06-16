using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// When the buffer clamp fires, context.WorkingScale must equal the allocated target.Scale.
[NonParallelizable]
[TestFixture]
public class WorkingScaleClampConsistencyTests
{
    // 4000 logical px × w 8 = 32000 px > MaxBufferDimension (16384) → the clamp must fire.
    private static readonly Rect s_pathologicalBounds = new(0, 0, 4000, 10);

    [Test]
    public void Flush_ClampWriteback_KeepsWorkingScaleEqualToBufferDensity()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            RenderNodeOperation op = RenderNodeOperation.CreateLambda(
                s_pathologicalBounds,
                canvas => canvas.DrawRectangle(s_pathologicalBounds, Brushes.Resource.White, null),
                hitTest: _ => false);

            using var targets = new EffectTargets { new EffectTarget(op) };
            using var builder = new SKImageFilterBuilder();
            using var activator = new FilterEffectActivator(
                targets, builder, outputScale: 1f, workingScale: 8f, maxWorkingScale: 8f);

            activator.Flush();

            float expected = RenderNodeContext.ClampWorkingScaleToBufferBudget(s_pathologicalBounds, 8f);
            Assert.That(expected, Is.LessThan(8f), "the fixture must actually trigger the clamp");
            Assert.That(activator.WorkingScale, Is.EqualTo(expected));
            Assert.That(activator.CurrentTargets, Has.Count.EqualTo(1));
            Assert.That(activator.CurrentTargets[0].Scale.Value, Is.EqualTo(activator.WorkingScale),
                "the flushed buffer's density and the activator's WorkingScale must agree");
            Assert.That(activator.CurrentTargets[0].RenderTarget!.Width,
                Is.LessThanOrEqualTo(RenderNodeContext.MaxBufferDimension));
        });
    }

    [Test]
    public void CreateTarget_ClampsInsteadOfFailing_AndTagsTrueDensity()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var targets = new EffectTargets();
            var context = new CustomFilterEffectContext(targets, outputScale: 1f, workingScale: 8f);

            using EffectTarget target = context.CreateTarget(s_pathologicalBounds);

            Assert.That(target.IsEmpty, Is.False,
                "an oversized request must degrade density, not return an empty target");
            float expected = RenderNodeContext.ClampWorkingScaleToBufferBudget(s_pathologicalBounds, 8f);
            Assert.That(target.Scale.Value, Is.EqualTo(expected));
            Assert.That(target.RenderTarget!.Width, Is.LessThanOrEqualTo(RenderNodeContext.MaxBufferDimension));
        });
    }

    [Test]
    public void Flush_PreviewAllocationFailure_DropsTargetWithoutThrowing()
    {
        using var targets = CreateInvalidFlushTargets();
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets, builder, outputScale: 1f, workingScale: 1f, maxWorkingScale: 8f);

        Assert.That(() => activator.Flush(), Throws.Nothing);
        Assert.That(activator.CurrentTargets, Is.Empty);
    }

    [Test]
    public void Flush_DeliveryAllocationFailure_ThrowsInsteadOfDroppingTarget()
    {
        using var targets = CreateInvalidFlushTargets();
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets, builder, outputScale: 1f, workingScale: 1f, maxWorkingScale: float.PositiveInfinity);

        var ex = Assert.Throws<InvalidOperationException>(() => activator.Flush());
        Assert.That(ex!.Message, Does.Contain("Effect flush buffer allocation failed"));
    }

    private static EffectTargets CreateInvalidFlushTargets()
    {
        RenderNodeOperation op = RenderNodeOperation.CreateLambda(
            new Rect(0, 0, -1, 10),
            _ => { },
            hitTest: _ => false);

        return new EffectTargets { new EffectTarget(op) };
    }
}
