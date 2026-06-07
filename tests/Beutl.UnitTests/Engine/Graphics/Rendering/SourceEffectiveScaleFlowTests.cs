using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// feature 003 (FR-018, supply-driven model): a bitmap source op carries a CONCRETE supply density. Today the
// built-in image/video sources only emit At(1), but a future proxy/high-res source will emit At(0.5) (a low-res
// proxy) or At(2.0) (a 4K source on a 1080 timeline). These integration tests feed a concrete At(d) source op
// through a REAL Custom effect (whose output op is tagged At(w)) and assert the working scale resolves to the
// source's supply density end-to-end — R1 (a 0.5 proxy is NOT upsampled to the output) and R2 (a 2.0 source is
// preserved). The ResolveWorkingScale MATH is unit-tested separately; this guards the actual node pipeline.
[NonParallelizable]
public class SourceEffectiveScaleFlowTests
{
    private static RenderNodeOperation SourceOp(float density)
        => RenderNodeOperation.CreateLambda(
            new Rect(0, 0, 120, 90),
            canvas => canvas.DrawRectangle(new Rect(0, 0, 120, 90), Brushes.Resource.White, null),
            hitTest: _ => false,
            effectiveScale: EffectiveScale.At(density));

    private static FilterEffectRenderNode MosaicNode()
    {
        var mosaic = new MosaicEffect();
        mosaic.TileSize.CurrentValue = new Size(10, 10);
        return new FilterEffectRenderNode(mosaic.ToResource(CompositionContext.Default));
    }

    [TestCase(0.5f)] // R1: a low-res proxy stays 0.5 — not upsampled to the 1.0 output.
    [TestCase(1.0f)]
    [TestCase(2.0f)] // R2: a high-density source stays 2.0 — preserved through the effect.
    public void ConcreteAtSource_ResolvesWorkingScaleToSupplyDensity(float density)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using FilterEffectRenderNode node = MosaicNode();
            var context = new RenderNodeContext([SourceOp(density)], outputScale: 1.0f);

            RenderNodeOperation[] ops = node.Process(context);

            Assert.That(ops, Is.Not.Empty, "the effect dropped the input op");
            // Inherit runs at the supply density, so the effect output carries the source's density as w.
            Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(density).Within(1e-4),
                $"At({density}) source did not flow through the effect at its supply density");

            // A non-unit density must be carried CONCRETELY (w == 1 collapses to the Unbounded byte-identity path).
            if (density != 1.0f)
            {
                Assert.That(ops[0].EffectiveScale.IsUnbounded, Is.False,
                    "a non-unit source density was lost (treated as re-rasterizable vector)");
            }

            foreach (RenderNodeOperation op in ops)
            {
                op.Dispose();
            }
        });
    }

    // The output scale is NOT a ceiling on the working scale (FR-016/FR-036): even at a reduced output (0.5), a
    // 2.0 source flows at 2.0 through the effect, downsampled only at the final stage.
    [Test]
    public void HighDensitySource_NotClampedByReducedOutputScale()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using FilterEffectRenderNode node = MosaicNode();
            var context = new RenderNodeContext([SourceOp(2.0f)], outputScale: 0.5f);

            RenderNodeOperation[] ops = node.Process(context);

            Assert.That(ops, Is.Not.Empty);
            Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(2.0f).Within(1e-4),
                "a 2.0 source was clamped down by the 0.5 output scale — s_out must not cap an intermediate");

            foreach (RenderNodeOperation op in ops)
            {
                op.Dispose();
            }
        });
    }
}
