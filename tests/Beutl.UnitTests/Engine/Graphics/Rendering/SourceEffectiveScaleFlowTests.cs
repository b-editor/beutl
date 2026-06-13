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
[TestFixture]
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

            // A concrete source density is carried concretely through the effect (the effect buffer reports its
            // true At(w) density, including w == 1 — it is a bitmap, not re-rasterizable vector).
            Assert.That(ops[0].EffectiveScale.IsUnbounded, Is.False,
                "a concrete source density was lost (treated as re-rasterizable vector)");

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
    // and a 2.0 source stays 2.0 (it is not raised to a 4x output).
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

    // FR-018: at w == 1 an At(1)-tagged source and an Unbounded source must render identically — both take the
    // point-blit fast path (Value == 1f ≡ Unbounded), so tagging a unit-density source concretely costs nothing.
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
        using (var canvas = new ImmediateCanvas(target, 1f))
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

    // feature 003 (FR-019, coherent density model): TransformRenderNode RE-SCALES a bitmap child's supply
    // density by the inverse of the transform scale, because density is "backing px per logical unit". A 0.5×
    // shrink packs the same pixels into half the logical space → density DOUBLES (R2: a high-res source dropped
    // small carries its detail into a downstream effect). A 2× enlarge HALVES it (no detail is fabricated).
    // A pure rotation / translation has scale 1 and leaves the density unchanged. Pure Process — no GPU.
    [TestCase(0.5f, 2.0f, 4.0f)] // shrink 0.5× : At(2) → At(4)  (R2)
    [TestCase(2.0f, 2.0f, 1.0f)] // enlarge 2× : At(2) → At(1)
    [TestCase(1.0f, 2.0f, 2.0f)] // identity scale: density unchanged
    [TestCase(0.25f, 1.0f, 4.0f)] // shrink 0.25× : At(1) → At(4)
    public void TransformRenderNode_ScalesChildDensity_ByInverseScale(float scale, float density, float expected)
    {
        var transform = new TransformRenderNode(Matrix.CreateScale(scale, scale), TransformOperator.Prepend);

        RenderNodeOperation[] ops = transform.Process(new RenderNodeContext([SourceOp(density)]));
        Assert.That(ops[0].EffectiveScale.IsUnbounded, Is.False);
        Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(expected).Within(1e-4),
            $"Scale({scale}) on At({density}) must resolve to At({expected}) (density = px / logical unit)");
        DisposeAll(ops);
    }

    // An anisotropic transform projects onto the axis that preserves the MOST detail (the smallest scale factor
    // → the highest density), so a single-float density never under-samples either axis.
    [Test]
    public void TransformRenderNode_AnisotropicScale_TakesDensestAxis()
    {
        var transform = new TransformRenderNode(Matrix.CreateScale(0.5f, 0.25f), TransformOperator.Prepend);

        RenderNodeOperation[] ops = transform.Process(new RenderNodeContext([SourceOp(1.0f)]));
        // min(0.5, 0.25) = 0.25 → At(1 / 0.25) = At(4): the 0.25× axis needs density 4 to keep its detail.
        Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(4.0f).Within(1e-4),
            "an anisotropic transform must project to the densest (most-shrunk) axis");
        DisposeAll(ops);
    }

    // A rotation alone carries no scale, so a bitmap's density survives a rotate unchanged.
    [Test]
    public void TransformRenderNode_PureRotation_LeavesDensityUnchanged()
    {
        var transform = new TransformRenderNode(Matrix.CreateRotation(MathF.PI / 4f), TransformOperator.Prepend);

        RenderNodeOperation[] ops = transform.Process(new RenderNodeContext([SourceOp(2.0f)]));
        Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(2.0f).Within(1e-4),
            "a pure rotation must not change the supply density");
        DisposeAll(ops);
    }

    // A degenerate transform (zero / non-finite scale) must NOT corrupt the density: a singular matrix can't be
    // decomposed and an infinite factor would yield At(d / ∞) = At(0) → a zero working scale downstream. The
    // density falls back to the child's unchanged value rather than producing 0 / NaN / ∞.
    [Test]
    public void TransformRenderNode_DegenerateScale_LeavesDensityUnchanged()
    {
        // Zero scale: singular matrix, TryDecomposeTransform returns false → density unchanged.
        var zero = new TransformRenderNode(Matrix.CreateScale(0f, 0f), TransformOperator.Prepend);
        RenderNodeOperation[] z = zero.Process(new RenderNodeContext([SourceOp(2.0f)]));
        Assert.That(z[0].EffectiveScale.Value, Is.EqualTo(2.0f).Within(1e-4));
        Assert.That(float.IsFinite(z[0].EffectiveScale.Value), Is.True);
        DisposeAll(z);

        // Non-finite scale: the factor is rejected (IsFinite guard) → density unchanged, never At(0).
        var inf = new TransformRenderNode(
            Matrix.CreateScale(float.PositiveInfinity, float.PositiveInfinity), TransformOperator.Prepend);
        RenderNodeOperation[] f = inf.Process(new RenderNodeContext([SourceOp(2.0f)]));
        Assert.That(f[0].EffectiveScale.Value, Is.EqualTo(2.0f).Within(1e-4),
            "an infinite transform scale must not collapse the density to At(0)");
        DisposeAll(f);
    }

    // Vector content re-rasterizes at any scale, so a transform never binds it to a density.
    [Test]
    public void TransformRenderNode_VectorChild_StaysUnbounded()
    {
        var transform = new TransformRenderNode(Matrix.CreateScale(0.5f, 0.5f), TransformOperator.Prepend);

        var vectorOp = RenderNodeOperation.CreateLambda(new Rect(0, 0, 10, 10), _ => { }, _ => false);
        RenderNodeOperation[] ops = transform.Process(new RenderNodeContext([vectorOp]));
        Assert.That(ops[0].EffectiveScale.IsUnbounded, Is.True, "a vector child must stay Unbounded through a transform");
        DisposeAll(ops);
    }

    // feature 003 (FR-004/FR-019b): a custom effect immediately followed by a Skia filter (e.g. [Mosaic, Blur])
    // leaves FilterEffectRenderNode's HasFilter() branch running over a FLUSHED At(w) buffer. The emitted op MUST
    // report that concrete At(w) density, never the re-rasterizable Unbounded — otherwise a parent boundary
    // mistakes the bitmap for vector and re-rasterizes it above the pixels it actually has. At density 1.0 the
    // distinction is still observable (At(1).IsUnbounded == false), so all three cases guard the fix.
    [TestCase(0.5f)]
    [TestCase(1.0f)]
    [TestCase(2.0f)]
    public void CustomThenSkiaChain_ReportsConcreteDensity_NotUnbounded(float density)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var group = new FilterEffectGroup();
            var mosaic = new MosaicEffect();
            mosaic.TileSize.CurrentValue = new Size(10, 10);
            var blur = new Blur();
            blur.Sigma.CurrentValue = new Size(3, 3);
            group.Children.Add(mosaic);
            group.Children.Add(blur);

            using var node = new FilterEffectRenderNode(group.ToResource(CompositionContext.Default));
            var context = new RenderNodeContext([SourceOp(density)], outputScale: 1.0f);

            RenderNodeOperation[] ops = node.Process(context);

            Assert.That(ops, Is.Not.Empty, "the [Mosaic, Blur] chain dropped the input op");
            Assert.That(ops[0].EffectiveScale.IsUnbounded, Is.False,
                "a flushed At(w) buffer behind a trailing Skia filter was over-reported as re-rasterizable Unbounded");
            Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(density).Within(1e-4),
                $"the [Mosaic, Blur] chain lost the At({density}) supply density");

            DisposeAll(ops);
        });
    }

    // feature 003 (FR-037(b)): a contour-based effect (StrokeEffect / FlatShadow) that INFLATES its output
    // bounds can size the effect buffer past the GPU per-axis limit, so CreateTarget clamps the working scale
    // down. The effect then maps its input-density construction onto the smaller buffer (StrokeEffect draws the
    // logical border under CreateScale(wOut); FlatShadow scales the whole device-px shadow by wOut/w), so the
    // result is not clipped and the op reports the clamped supply density. A huge one-axis pen Offset trips the
    // per-axis limit while keeping the buffer's OTHER axis small (~16384 × 100 px ≈ a few MiB), so the test
    // stays allocatable. Guards the end-to-end clamp path: the working scale is reduced below the nominal and
    // nothing throws.
    [Test]
    public void StrokeEffect_OverBudgetBounds_ClampsWorkingScaleBelowNominal_DoesNotThrow()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var pen = new Pen();
            pen.Thickness.CurrentValue = 8;
            pen.Brush.CurrentValue = Brushes.Red;
            var stroke = new StrokeEffect();
            stroke.Pen.CurrentValue = pen;
            // Inflate ONLY the X axis past the 16384 per-axis limit at the nominal working scale 1.0, so the
            // clamped buffer is ~16384 wide but stays short on Y (allocatable).
            stroke.Offset.CurrentValue = new Point(20000, 0);

            using var node = new FilterEffectRenderNode(stroke.ToResource(CompositionContext.Default));

            RenderNodeOperation[] ops = null!;
            Assert.DoesNotThrow(() =>
            {
                ops = node.Process(new RenderNodeContext([SourceOp(1f)], outputScale: 1f));
            });

            Assert.That(ops, Is.Not.Empty, "the over-budget StrokeEffect dropped its op");
            Assert.That(ops[0].EffectiveScale.Value, Is.GreaterThan(0f));
            Assert.That(ops[0].EffectiveScale.Value, Is.LessThan(1f),
                "the over-budget stroke bounds did not clamp the working scale below the nominal 1.0");

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
