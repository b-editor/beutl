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

// Integration tests: concrete At(d) source through a real Custom effect, asserting w = max(s_out, supply).
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

    [TestCase(0.5f, 1.0f)] // sub-output supply floored to the deliverable 1.0
    [TestCase(1.0f, 1.0f)]
    [TestCase(2.0f, 2.0f)] // high-density source stays 2.0 (supply wins)
    public void ConcreteAtSource_ResolvesWorkingScaleToSupplyOrOutputFloor(float density, float expectedW)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using FilterEffectRenderNode node = MosaicNode();
            var context = new RenderNodeContext([SourceOp(density)], outputScale: 1.0f);

            RenderNodeOperation[] ops = node.Process(context);

            Assert.That(ops, Is.Not.Empty, "the effect dropped the input op");
            Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(expectedW).Within(1e-4),
                $"At({density}) source resolved the wrong working scale");

            Assert.That(ops[0].EffectiveScale.IsUnbounded, Is.False,
                "a concrete source density was lost (treated as re-rasterizable vector)");

            foreach (RenderNodeOperation op in ops)
            {
                op.Dispose();
            }
        });
    }

    // Output scale is not a ceiling: a 2.0 source at 0.5 output still flows at 2.0.
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

    // w = max(s_out, supply) at supersample outputs.
    [TestCase(0.5f, 2.0f, 2.0f)] // sub-output proxy floored to 2.0
    [TestCase(1.0f, 2.0f, 2.0f)] // 1:1 source floored to 2.0
    [TestCase(2.0f, 2.0f, 2.0f)] // supply matches output
    [TestCase(2.0f, 4.0f, 4.0f)] // supply below output: floored to 4.0
    [TestCase(2.0f, 1.5f, 2.0f)] // supply above output: supply wins
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

    // MaxWorkingScale caps the resolved working scale through the real node.
    [TestCase(float.PositiveInfinity, 4.0f)] // export: no ceiling
    [TestCase(2.0f, 2.0f)]                   // preview: ceiling caps 4.0 source to 2.0
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

    // At(1) and Unbounded must render identically at w == 1.
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
                "At(1) source diverged from Unbounded at s_out == 1");
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

    // TransformRenderNode rescales a bitmap child's density by the inverse transform scale.
    [TestCase(0.5f, 2.0f, 4.0f)] // shrink 0.5x: At(2) -> At(4)
    [TestCase(2.0f, 2.0f, 1.0f)] // enlarge 2x: At(2) -> At(1)
    [TestCase(1.0f, 2.0f, 2.0f)] // identity: unchanged
    [TestCase(0.25f, 1.0f, 4.0f)] // shrink 0.25x: At(1) -> At(4)
    public void TransformRenderNode_ScalesChildDensity_ByInverseScale(float scale, float density, float expected)
    {
        var transform = new TransformRenderNode(Matrix.CreateScale(scale, scale), TransformOperator.Prepend);

        RenderNodeOperation[] ops = transform.Process(new RenderNodeContext([SourceOp(density)]));
        Assert.That(ops[0].EffectiveScale.IsUnbounded, Is.False);
        Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(expected).Within(1e-4),
            $"Scale({scale}) on At({density}) must resolve to At({expected}) (density = px / logical unit)");
        DisposeAll(ops);
    }

    // An anisotropic transform projects onto the densest axis (smallest scale factor).
    [Test]
    public void TransformRenderNode_AnisotropicScale_TakesDensestAxis()
    {
        var transform = new TransformRenderNode(Matrix.CreateScale(0.5f, 0.25f), TransformOperator.Prepend);

        RenderNodeOperation[] ops = transform.Process(new RenderNodeContext([SourceOp(1.0f)]));
        // min(0.5, 0.25) = 0.25 -> At(1 / 0.25) = At(4)
        Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(4.0f).Within(1e-4),
            "an anisotropic transform must project to the densest (most-shrunk) axis");
        DisposeAll(ops);
    }

    // Pure rotation leaves density unchanged.
    [Test]
    public void TransformRenderNode_PureRotation_LeavesDensityUnchanged()
    {
        var transform = new TransformRenderNode(Matrix.CreateRotation(MathF.PI / 4f), TransformOperator.Prepend);

        RenderNodeOperation[] ops = transform.Process(new RenderNodeContext([SourceOp(2.0f)]));
        Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(2.0f).Within(1e-4),
            "a pure rotation must not change the supply density");
        DisposeAll(ops);
    }

    // A degenerate transform (zero / non-finite scale) must not corrupt the density.
    [Test]
    public void TransformRenderNode_DegenerateScale_LeavesDensityUnchanged()
    {
        // Zero scale: singular matrix, density unchanged.
        var zero = new TransformRenderNode(Matrix.CreateScale(0f, 0f), TransformOperator.Prepend);
        RenderNodeOperation[] z = zero.Process(new RenderNodeContext([SourceOp(2.0f)]));
        Assert.That(z[0].EffectiveScale.Value, Is.EqualTo(2.0f).Within(1e-4));
        Assert.That(float.IsFinite(z[0].EffectiveScale.Value), Is.True);
        DisposeAll(z);

        // Non-finite scale: density unchanged, never At(0).
        var inf = new TransformRenderNode(
            Matrix.CreateScale(float.PositiveInfinity, float.PositiveInfinity), TransformOperator.Prepend);
        RenderNodeOperation[] f = inf.Process(new RenderNodeContext([SourceOp(2.0f)]));
        Assert.That(f[0].EffectiveScale.Value, Is.EqualTo(2.0f).Within(1e-4),
            "an infinite transform scale must not collapse the density to At(0)");
        DisposeAll(f);
    }

    // Vector content stays Unbounded through a transform.
    [Test]
    public void TransformRenderNode_VectorChild_StaysUnbounded()
    {
        var transform = new TransformRenderNode(Matrix.CreateScale(0.5f, 0.5f), TransformOperator.Prepend);

        var vectorOp = RenderNodeOperation.CreateLambda(new Rect(0, 0, 10, 10), _ => { }, _ => false);
        RenderNodeOperation[] ops = transform.Process(new RenderNodeContext([vectorOp]));
        Assert.That(ops[0].EffectiveScale.IsUnbounded, Is.True, "a vector child must stay Unbounded through a transform");
        DisposeAll(ops);
    }

    // CustomTransformRenderNode must rescale density identically to TransformRenderNode.
    [Test]
    public void CustomTransformRenderNode_ScalesChildDensity_LikeTransformRenderNode()
    {
        Transform.Resource scale = new ScaleTransform(50, 50).ToResource(CompositionContext.Default); // 0.5x both axes
        using var node = new DrawableGroup.CustomTransformRenderNode(
            scale, default, new Size(120, 90), AlignmentX.Left, AlignmentY.Top,
            new MemoryNode<Rect>(new Rect(0, 0, 120, 90)));

        RenderNodeOperation[] ops = node.Process(new RenderNodeContext([SourceOp(2.0f)]));
        Assert.That(ops[0].EffectiveScale.IsUnbounded, Is.False,
            "the group/decorator transform dropped a concrete density to Unbounded");
        // 0.5x shrink doubles density: At(2) -> At(4)
        Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(4.0f).Within(1e-4),
            "CustomTransformRenderNode must rescale density by the inverse transform scale, like TransformRenderNode");
        DisposeAll(ops);
    }

    // Vector child stays Unbounded through the element wrapper too.
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

    // A Custom+Skia chain (e.g. [Mosaic, Blur]) must report concrete At(w), not Unbounded.
    [TestCase(0.5f, 1.0f)]
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

    // An over-budget StrokeEffect (bounds exceed GPU per-axis limit) must clamp working scale down.
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
            // Inflate X past the 16384 per-axis limit at w=1, keeping Y short (allocatable).
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

    // Escape hatch: a FilterEffectRenderNode subclass that overrides Process to use a non-supply w.
    private sealed class ClampToOutputRenderNode(FilterEffect.Resource fe) : FilterEffectRenderNode(fe)
    {
        public override RenderNodeOperation[] Process(RenderNodeContext context)
        {
            var scales = context.Input.Select(i => i.EffectiveScale).ToArray();
            float supplyW = RenderNodeContext.ResolveWorkingScale(scales, context.OutputScale, context.MaxWorkingScale);
            float clampedW = MathF.Min(supplyW, context.OutputScale);
            return context.Input.Select(input => RenderNodeOperation.CreateLambda(
                    input.Bounds,
                    input.Render,
                    hitTest: input.HitTest,
                    onDispose: input.Dispose,
                    effectiveScale: EffectiveScale.At(clampedW)))
                .ToArray();
        }
    }

    // Custom render node overrides supply-driven w with clamp-to-output.
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

    // End-to-end escape hatch: runs a real Mosaic at w = max(supplyDriven, 2 * s_out) (SSAA-on-demand).
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
            // Oversample to 2x the deliverable density, bounded by the global ceiling.
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

    // End-to-end: OversampleMosaicRenderNode runs the real Mosaic at w = 2 * s_out = 2.0. GPU-gated.
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
            // At(1) source at s_out 1.0: supply-driven gives w=1; oversample hatch lifts to 2.0.
            var context = new RenderNodeContext([SourceOp(1.0f)], outputScale: 1.0f);

            RenderNodeOperation[] ops = node.Process(context);

            Assert.That(ops, Is.Not.Empty, "the oversample escape hatch dropped its op — the effect did not apply");
            Assert.That(ops[0].EffectiveScale.Value, Is.EqualTo(2.0f).Within(1e-4),
                "the oversample hatch must run the effect at w = 2 * s_out");
            Assert.That(ops[0].EffectiveScale.IsUnbounded, Is.False,
                "an oversampled effect buffer must be concrete At(w), not Unbounded");

            // Render into a real target to verify the flush/blit path executes.
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
            // Vacuity guard: the oversampled Mosaic must leave visible content (not all-black).
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
