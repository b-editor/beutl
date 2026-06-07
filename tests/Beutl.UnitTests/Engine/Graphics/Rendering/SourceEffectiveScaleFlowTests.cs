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

            DisposeAll(ops);
        });
    }

    // OutputScale >= 1 (export supersampling). Under Inherit, w == the source supply density regardless of the
    // output scale — the output is NEITHER a ceiling NOR a floor for a concrete source. So a 0.5 proxy stays 0.5
    // even when the frame is exported at 2x SSAA (it is NOT fake-upsampled to fabricate detail it does not have),
    // and a 2.0 source stays 2.0 (it is not raised to a 4x output). w == 1 collapses to the Unbounded byte path.
    [TestCase(0.5f, 2.0f, 0.5f)] // proxy in a 2x SSAA export: NOT upsampled
    [TestCase(1.0f, 2.0f, 1.0f)] // 1:1 source in a 2x SSAA export: caps at native (the effect-SSAA tradeoff)
    [TestCase(2.0f, 2.0f, 2.0f)] // 2.0 source matches the 2x output
    [TestCase(2.0f, 4.0f, 2.0f)] // 2.0 source below a 4x output: NOT upsampled to 4
    [TestCase(2.0f, 1.5f, 2.0f)] // non-integer output > 1
    public void ConcreteAtSource_AtSupersampleOutput_StaysAtSupplyDensity(float density, float outputScale, float expectedW)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using FilterEffectRenderNode node = MosaicNode();
            var context = new RenderNodeContext([SourceOp(density)], outputScale: outputScale);

            RenderNodeOperation[] ops = node.Process(context);

            Assert.That(ops, Is.Not.Empty);
            Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(expectedW).Within(1e-4),
                $"At({density}) @ outputScale {outputScale} resolved the wrong working scale");
            if (expectedW != 1.0f)
            {
                Assert.That(ops[0].EffectiveScale.IsUnbounded, Is.False,
                    "a non-unit source density was lost at supersample output");
            }

            DisposeAll(ops);
        });
    }

    // FR-037 wiring: the FilterEffectRenderNode reads context.MaxWorkingScale and caps the resolved working scale.
    // An At(4) source under a 2x ceiling (preview) resolves to w == 2 through the real node, vs w == 4 uncapped.
    [TestCase(float.PositiveInfinity, 4.0f)] // export: no ceiling
    [TestCase(2.0f, 2.0f)]                   // preview: 2x s_out ceiling caps a 4.0 source to 2.0
    public void MaxWorkingScale_CapsThroughTheNode(float maxWorkingScale, float expectedW)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using FilterEffectRenderNode node = MosaicNode();
            var context = new RenderNodeContext([SourceOp(4.0f)], outputScale: 1.0f, maxWorkingScale: maxWorkingScale);

            RenderNodeOperation[] ops = node.Process(context);

            Assert.That(ops, Is.Not.Empty);
            Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(expectedW).Within(1e-4),
                $"maxWorkingScale {maxWorkingScale} did not cap the working scale through the node");

            DisposeAll(ops);
        });
    }

    // An effect can still opt into supersampling ABOVE a low source via Oversample (SSAA-on-demand): a 0.5 proxy
    // through an Oversample(2) policy at a 2x output runs at max(0.5, 2*2) = 4. This is the effect's choice, not
    // the source's density — it shows OutputScale >= 1 DOES raise w when the policy asks for it. Pure math (no GPU).
    [Test]
    public void OversamplePolicy_RaisesWorkingScaleAboveSource_AtSupersampleOutput()
    {
        float w = RenderNodeContext.ResolveWorkingScale(
            [EffectiveScale.At(0.5f)], outputScale: 2.0f, ResolutionPolicy.Oversample(2f));
        Assert.That(w, Is.EqualTo(4.0f).Within(1e-4),
            "Oversample(2) at a 2x output must force w = 4 even from a 0.5 source");
    }

    // FR-018 byte-identity: at s_out == 1 the At(1) source change must be byte-identical to the old Unbounded.
    // The golden suite only uses vector shapes, so it never exercises an At-tagged source — feed the SAME content
    // through the SAME effect once tagged At(1) and once Unbounded and assert the rendered pixels are identical.
    [Test]
    public void At1Source_IsByteIdenticalToUnbounded_AtOutputScale1()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap at1 = RenderThroughMosaicAtScale1(EffectiveScale.At(1f));
            using Bitmap unbounded = RenderThroughMosaicAtScale1(EffectiveScale.Unbounded);

            ReadOnlySpan<byte> a = at1.GetPixelSpan();
            ReadOnlySpan<byte> b = unbounded.GetPixelSpan();
            Assert.That(a.SequenceEqual(b), Is.True,
                "At(1) source diverged from Unbounded at s_out == 1 — the FR-018 byte-identity claim is broken");
        });
    }

    private static Bitmap RenderThroughMosaicAtScale1(EffectiveScale srcScale)
    {
        var srcOp = RenderNodeOperation.CreateLambda(
            new Rect(0, 0, 120, 90),
            canvas => canvas.DrawRectangle(new Rect(0, 0, 120, 90), Brushes.Resource.White, null),
            hitTest: _ => false,
            onDispose: null,
            effectiveScale: srcScale);

        using FilterEffectRenderNode node = MosaicNode();
        RenderNodeOperation[] ops = node.Process(new RenderNodeContext([srcOp], outputScale: 1f));

        using RenderTarget target = RenderTarget.Create(120, 90)!;
        using (var canvas = new ImmediateCanvas(target) { OutputScale = 1f })
        {
            canvas.Clear(Colors.Black);
            foreach (RenderNodeOperation op in ops)
            {
                op.Render(canvas);
                op.Dispose();
            }
        }

        return target.Snapshot();
    }

    // TransformRenderNode forwards (does NOT scale) the child's supply density: a pure-CTM transform does not
    // re-rasterize a bitmap-backed child, so At(d) stays At(d) — NOT Unbounded (the old drop, which mis-tagged a
    // bitmap buffer as re-rasterizable) and NOT At(d/scale) (which would change s_out==1 output). Vector stays
    // Unbounded. Pure Process — no GPU.
    [Test]
    public void TransformRenderNode_ForwardsChildEffectiveScale_Unscaled()
    {
        var transform = new TransformRenderNode(Matrix.CreateScale(0.5f, 0.5f), TransformOperator.Prepend);

        RenderNodeOperation[] concrete = transform.Process(new RenderNodeContext([SourceOp(2.0f)]));
        Assert.That(concrete[0].EffectiveScale.IsUnbounded, Is.False);
        Assert.That(concrete[0].EffectiveScale.Value, Is.EqualTo(2.0f).Within(1e-4),
            "transform must forward the density UNCHANGED (scaling it would break s_out==1 byte-identity)");
        DisposeAll(concrete);

        var vectorOp = RenderNodeOperation.CreateLambda(new Rect(0, 0, 10, 10), _ => { }, _ => false);
        RenderNodeOperation[] vector = transform.Process(new RenderNodeContext([vectorOp]));
        Assert.That(vector[0].EffectiveScale.IsUnbounded, Is.True, "a vector child must stay Unbounded through a transform");
        DisposeAll(vector);
    }

    private static void DisposeAll(RenderNodeOperation[] ops)
    {
        foreach (RenderNodeOperation op in ops)
        {
            op.Dispose();
        }
    }
}
