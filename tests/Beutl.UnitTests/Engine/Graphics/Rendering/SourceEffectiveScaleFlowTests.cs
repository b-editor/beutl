using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// feature 003 (FR-018, supply-driven model): a bitmap source op carries a CONCRETE supply density. Today the
// built-in image/video sources only emit At(1), but a future proxy/high-res source will emit At(0.5) (a low-res
// proxy) or At(2.0) (a 4K source on a 1080 timeline). These integration tests feed a concrete At(d) source op
// through a REAL Custom effect (whose output op is tagged At(w)) and assert the working scale resolves to
// w = max(s_out, supply) end-to-end: s_out is a FLOOR (a sub-output supply is lifted to the deliverable density)
// and never a ceiling (R2 — a 2.0 source is preserved). The ResolveWorkingScale MATH is unit-tested separately;
// this guards the actual node pipeline.
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

    [TestCase(0.5f, 1.0f)] // sub-output supply (enlarged bitmap) floored to the deliverable 1.0
    [TestCase(1.0f, 1.0f)]
    [TestCase(2.0f, 2.0f)] // R2: a high-density source stays 2.0 — preserved through the effect (supply wins).
    public void ConcreteAtSource_ResolvesWorkingScaleToSupplyOrOutputFloor(float density, float expectedW)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using FilterEffectRenderNode node = MosaicNode();
            var context = new RenderNodeContext([SourceOp(density)], outputScale: 1.0f);

            RenderNodeOperation[] ops = node.Process(context);

            Assert.That(ops, Is.Not.Empty, "the effect dropped the input op");
            // w = max(s_out, supply): the effect runs at the densest of the deliverable floor and the supply.
            Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(expectedW).Within(1e-4),
                $"At({density}) source resolved the wrong working scale (s_out floor + supply max)");

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

    // OutputScale >= 1 (export supersampling). w = max(s_out, supply): the output scale is a FLOOR (an effect
    // never runs below the deliverable density) and never a ceiling (a denser supply runs above it). So a 0.5
    // proxy in a 2x SSAA export is floored to 2.0 — the user asked for 2x delivery, so the effect's own working
    // resolution is 2.0; the source's 0.5 detail is a separate, unchanged limit (no fabricated source detail).
    // A 2.0 source in a 1.5 output keeps 2.0 (supply wins, s_out is not a ceiling).
    [TestCase(0.5f, 2.0f, 2.0f)] // sub-output proxy in a 2x SSAA export: floored to the deliverable 2.0
    [TestCase(1.0f, 2.0f, 2.0f)] // 1:1 source in a 2x SSAA export: floored to 2.0
    [TestCase(2.0f, 2.0f, 2.0f)] // 2.0 source matches the 2x output
    [TestCase(2.0f, 4.0f, 4.0f)] // 2.0 source below a 4x output: floored UP to the 4.0 deliverable
    [TestCase(2.0f, 1.5f, 2.0f)] // non-integer output below supply: supply wins
    public void ConcreteAtSource_AtSupersampleOutput_ResolvesMaxOfSupplyAndOutput(float density, float outputScale, float expectedW)
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

    // feature 003 (FR-019): the element-level wrapper (DrawableGroup / DrawableDecorator) is ALSO a transform
    // boundary, so CustomTransformRenderNode MUST re-scale a bitmap child's density exactly like
    // TransformRenderNode — both call the shared TransformRenderNode.RescaleDensity. Without this, the same
    // scale carried on a group/decorator vs a leaf drawable would resolve a downstream effect at a DIFFERENT
    // working scale (the density model would be incoherent across the two transform nodes). Pure Process — no GPU.
    [Test]
    public void CustomTransformRenderNode_ScalesChildDensity_LikeTransformRenderNode()
    {
        Transform.Resource scale = new ScaleTransform(50, 50).ToResource(CompositionContext.Default); // 0.5× both axes
        using var node = new DrawableGroup.CustomTransformRenderNode(
            scale, default, new Size(120, 90), AlignmentX.Left, AlignmentY.Top,
            new MemoryNode<Rect>(new Rect(0, 0, 120, 90)));

        RenderNodeOperation[] ops = node.Process(new RenderNodeContext([SourceOp(2.0f)]));
        Assert.That(ops[0].EffectiveScale.IsUnbounded, Is.False,
            "the group/decorator transform dropped a concrete density to Unbounded (FR-019 not applied)");
        // 0.5× shrink doubles density: At(2) → At(4), identical to TransformRenderNode's rescale.
        Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(4.0f).Within(1e-4),
            "CustomTransformRenderNode must rescale density by the inverse transform scale, like TransformRenderNode");
        DisposeAll(ops);
    }

    // A vector child stays re-rasterizable through the element wrapper too (mirrors the TransformRenderNode case).
    [Test]
    public void CustomTransformRenderNode_VectorChild_StaysUnbounded()
    {
        Transform.Resource scale = new ScaleTransform(50, 50).ToResource(CompositionContext.Default);
        using var node = new DrawableGroup.CustomTransformRenderNode(
            scale, default, new Size(10, 10), AlignmentX.Left, AlignmentY.Top,
            new MemoryNode<Rect>(new Rect(0, 0, 10, 10)));

        var vectorOp = RenderNodeOperation.CreateLambda(new Rect(0, 0, 10, 10), _ => { }, _ => false);
        RenderNodeOperation[] ops = node.Process(new RenderNodeContext([vectorOp]));
        Assert.That(ops[0].EffectiveScale.IsUnbounded, Is.True,
            "a vector child must stay Unbounded through the group/decorator transform");
        DisposeAll(ops);
    }

    // feature 003 (FR-004/FR-019b): a custom effect immediately followed by a Skia filter (e.g. [Mosaic, Blur])
    // leaves FilterEffectRenderNode's HasFilter() branch running over a FLUSHED At(w) buffer. The emitted op MUST
    // report that concrete At(w) density, never the re-rasterizable Unbounded — otherwise a parent boundary
    // mistakes the bitmap for vector and re-rasterizes it above the pixels it actually has. At density 1.0 the
    // distinction is still observable (At(1).IsUnbounded == false), so all three cases guard the fix.
    [TestCase(0.5f, 1.0f)] // sub-output supply floored to the 1.0 deliverable, still concrete (not Unbounded)
    [TestCase(1.0f, 1.0f)]
    [TestCase(2.0f, 2.0f)]
    public void CustomThenSkiaChain_ReportsConcreteDensity_NotUnbounded(float density, float expectedW)
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
            Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(expectedW).Within(1e-4),
                $"the [Mosaic, Blur] chain resolved the wrong working scale for At({density})");

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

    // feature 003 (S5 / FR-036): the documented escape hatch for an effect that needs a NON-supply working scale
    // (the replacement for the removed ResolutionPolicy) is a FilterEffectRenderNode subclass returned from
    // FilterEffect.Resource.CreateRenderNode() that overrides Process and computes its own w. This minimal
    // subclass picks clamp-to-output (w = min(supply, s_out)) and re-tags its inputs at that w, proving an author
    // CAN select a non-supply working scale through the override point. (The full effect flow would feed this w
    // into FilterEffectContext/FilterEffectActivator; the w-SELECTION is the part a subclass customizes, which is
    // what this guards. Pure Process — no GPU.)
    private sealed class ClampToOutputRenderNode(FilterEffect.Resource fe) : FilterEffectRenderNode(fe)
    {
        public override RenderNodeOperation[] Process(RenderNodeContext context)
        {
            var scales = context.Input.Select(i => i.EffectiveScale).ToArray();
            float supplyW = RenderNodeContext.ResolveWorkingScale(scales, context.OutputScale, context.MaxWorkingScale);
            float clampedW = MathF.Min(supplyW, context.OutputScale); // the removed ResolutionPolicy.ClampToOutput
            return context.Input.Select(input => RenderNodeOperation.CreateLambda(
                    input.Bounds,
                    input.Render,
                    hitTest: input.HitTest,
                    onDispose: input.Dispose,
                    effectiveScale: EffectiveScale.At(clampedW)))
                .ToArray();
        }
    }

    // Prove the CreateRenderNode() escape hatch: a custom FilterEffectRenderNode chooses a working scale OTHER
    // than the supply-driven default. The built-in supply-driven path keeps a 2.0 source at w = 2
    // (ConcreteAtSource_ResolvesWorkingScaleToSupplyOrOutputFloor above, the [TestCase(2.0f, 2.0f)] row); this
    // clamp-to-output subclass instead resolves w = min(supply, s_out), so the same 2.0 source runs at the output.
    [TestCase(1.0f, 1.0f)] // supply 2 clamped to s_out 1
    [TestCase(0.5f, 0.5f)] // supply 2 clamped to s_out 0.5
    public void CustomRenderNode_OverridesSupplyDriven_WithClampToOutput(float outputScale, float expectedW)
    {
        var fe = new MosaicEffect().ToResource(CompositionContext.Default);
        using var node = new ClampToOutputRenderNode(fe);

        RenderNodeOperation[] ops = node.Process(new RenderNodeContext([SourceOp(2.0f)], outputScale: outputScale));

        Assert.That(ops, Is.Not.Empty);
        Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(expectedW).Within(1e-4),
            "the custom render node did not apply its clamp-to-output working scale — the CreateRenderNode() escape hatch is broken");
        DisposeAll(ops);
    }

    // feature 003 (S5 / FR-036): a GENUINELY-WORKING escape-hatch example — an effect that runs a REAL Mosaic at
    // a NON-supply working scale (SSAA-on-demand: w = max(supplyDriven, 2 × s_out)). Unlike ClampToOutputRenderNode
    // above (which only re-tags inputs and drops the effect body), this reproduces the full FilterEffectRenderNode
    // .Process flow so the Mosaic actually applies at the oversample density. Per the contract
    // (contracts/effect-scale-contract.md), base.Process recomputes the supply-driven w and IGNORES any w a
    // subclass computes, so an author that needs a different w must copy the Process body and change ONLY the
    // `workingScale =` line — which is exactly what this does. Threads the oversample w into FilterEffectContext +
    // FilterEffectActivator and tags the output op At(w), proving the hatch works end-to-end (not as a re-tag stub).
    private sealed class OversampleMosaicRenderNode(FilterEffect.Resource fe) : FilterEffectRenderNode(fe)
    {
        public override RenderNodeOperation[] Process(RenderNodeContext context)
        {
            if (FilterEffect == null || !FilterEffect.Value.Resource.IsEnabled)
            {
                return context.Input;
            }

            Span<EffectiveScale> inputScales = context.Input.Length <= 16
                ? stackalloc EffectiveScale[context.Input.Length]
                : new EffectiveScale[context.Input.Length];
            for (int i = 0; i < context.Input.Length; i++)
            {
                inputScales[i] = context.Input[i].EffectiveScale;
            }

            float supplyDriven = RenderNodeContext.ResolveWorkingScale(
                inputScales, context.OutputScale, context.MaxWorkingScale);
            // THE ONLY DEVIATION from the base Process body: oversample to 2× the deliverable density (SSAA-on-
            // demand), still bounded by the global ceiling so preview can cap it.
            float workingScale = MathF.Min(
                MathF.Max(supplyDriven, 2f * context.OutputScale), context.MaxWorkingScale);

            Rect bounds = context.CalculateBounds();
            workingScale = RenderNodeContext.ClampWorkingScaleToBufferBudget(bounds, workingScale);

            using var feContext = new FilterEffectContext(bounds, context.OutputScale, workingScale);
            FilterEffect.Value.Resource.GetOriginal().ApplyTo(feContext, FilterEffect.Value.Resource);
            var effectTargets = new EffectTargets();
            effectTargets.AddRange(context.Input.Select(i => new EffectTarget(i)));

            using (var builder = new SKImageFilterBuilder())
            using (var activator = new FilterEffectActivator(
                       effectTargets, builder, context.OutputScale, workingScale, context.MaxWorkingScale))
            {
                activator.Apply(feContext);

                if (builder.HasFilter())
                {
                    var imageFilter = builder.GetFilter();
                    return activator.CurrentTargets.Select(t =>
                    {
                        var paint = new SKPaint();
                        paint.ImageFilter = imageFilter;
                        return RenderNodeOperation.CreateLambda(
                            bounds: t.Bounds,
                            render: canvas =>
                            {
                                using (canvas.PushBlendMode(BlendMode.SrcOver))
                                using (canvas.PushTransform(Matrix.CreateTranslation(
                                           t.Bounds.X - t.OriginalBounds.X,
                                           t.Bounds.Y - t.OriginalBounds.Y)))
                                using (canvas.PushPaint(paint))
                                {
                                    t.Draw(canvas);
                                }
                            },
                            hitTest: t.Bounds.Contains,
                            onDispose: () =>
                            {
                                t.Dispose();
                                paint.Dispose();
                            },
                            effectiveScale: t.Scale);
                    }).ToArray();
                }
                else
                {
                    return activator.CurrentTargets.Select(i =>
                        i.NodeOperation ??
                        RenderNodeOperation.CreateFromRenderTarget(i.Bounds, i.Bounds.Position, i.RenderTarget!, i.Scale))
                        .ToArray();
                }
            }
        }
    }

    // Prove the CreateRenderNode() escape hatch works END-TO-END (not just as a re-tag stub): feeding a vector /
    // At(1) source at outputScale 1.0, the OversampleMosaicRenderNode runs the REAL Mosaic at w = 2 × s_out = 2.0
    // (oversampled ABOVE the supply, the SSAA-on-demand case). The built-in supply-driven path would resolve this
    // same input to w == 1.0; this subclass instead resolves 2.0 AND actually applies the effect (ops non-empty,
    // the op renders). GPU-gated (the real Mosaic flush allocates a device buffer).
    [Test]
    public void OversampleMosaicRenderNode_RunsRealEffect_AboveSupply_AtTwiceOutputScale()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var mosaic = new MosaicEffect();
            mosaic.TileSize.CurrentValue = new Size(10, 10);
            using FilterEffectRenderNode node =
                new OversampleMosaicRenderNode(mosaic.ToResource(CompositionContext.Default));
            // An At(1) source at s_out 1.0: supply-driven would give w == 1; the oversample hatch lifts it to 2.0.
            var context = new RenderNodeContext([SourceOp(1.0f)], outputScale: 1.0f);

            RenderNodeOperation[] ops = node.Process(context);

            Assert.That(ops, Is.Not.Empty, "the oversample escape hatch dropped its op — the effect did not apply");
            Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(2.0f).Within(1e-4),
                "the oversample hatch must run the effect ABOVE the supply at w = 2 × s_out (SSAA-on-demand)");
            Assert.That(ops[0].EffectiveScale.IsUnbounded, Is.False,
                "an oversampled effect buffer is a concrete At(w) bitmap, not re-rasterizable Unbounded");

            // The op actually applies the effect: render it into a real target so the flush/blit path executes.
            using RenderTarget target = RenderTarget.Create(120, 90)!;
            using (var canvas = new ImmediateCanvas(target, 1f))
            {
                canvas.Clear(Colors.Black);
                foreach (RenderNodeOperation op in ops)
                {
                    op.Render(canvas);
                }
            }

            using Bitmap snapshot = target.Snapshot();
            // The snapshot dimensions are structurally fixed by RenderTarget.Create(120, 90), so asserting them
            // proves nothing. Instead use a vacuity guard: the oversampled Mosaic of a WHITE source must leave
            // VISIBLE content — a silently-empty / no-op render would leave the black clear. (The real oversample
            // proof is the ops[0].EffectiveScale.Value == 2.0 assertion above.)
            using RenderTarget blackTarget = RenderTarget.Create(120, 90)!;
            using (var blackCanvas = new ImmediateCanvas(blackTarget))
                blackCanvas.Clear(Colors.Black);
            using Bitmap black = blackTarget.Snapshot();
            Assert.That(ImageMetrics.MeanAbsoluteError(snapshot, black), Is.GreaterThan(0.01),
                "the oversampled effect produced an all-black buffer (it silently failed to draw)");

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
