using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// feature 003 (FR-037(b) consistency): when the per-buffer dimension clamp fires, the density a
// custom effect's device math keys on (context.WorkingScale) must agree with the density its
// buffers were actually allocated at (target.Scale) — a mismatch shifts contours/crops/tiles
// systematically. These tests pin the two repaired sites: the uniform Flush clamp written back to
// FilterEffectActivator.WorkingScale, and the CreateTarget re-clamp (which previously allocated
// unclamped and degraded to an empty target + Open() throwing).
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
}
