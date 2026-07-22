using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// Integration tests: concrete At(d) sources through real recorded nodes, asserting
// w = min(max(s_out, supply), maxWorkingScale) at the resulting materialization boundary.
[NonParallelizable]
[TestFixture]
public class SourceEffectiveScaleFlowTests
{
    private static int s_throwingWorkingScaleResolverCalls;
    private static float s_legacyCustomWorkingScale;
    private static List<ScaleResolverObservation>? s_scaleResolverObservations;

    private static FilterEffectRenderNode MosaicNode()
    {
        var mosaic = new MosaicEffect();
        mosaic.TileSize.CurrentValue = new Size(10, 10);
        return new FilterEffectRenderNode(mosaic.ToResource(CompositionContext.Default));
    }

    [TestCase(0.5f, 1.0f)]
    [TestCase(1.0f, 1.0f)]
    [TestCase(2.0f, 2.0f)]
    public void ConcreteAtSource_ResolvesWorkingScaleToSupplyOrOutputFloor(float density, float expectedW)
    {
        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            MosaicNode(),
            EffectiveScale.At(density));

        Assert.That(measurement.HasFragments, Is.True, "the effect dropped the input fragment");
        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(expectedW).Within(1e-4),
            $"At({density}) source resolved the wrong working scale");
        Assert.That(measurement.EffectiveScale.IsUnbounded, Is.False,
            "a concrete source density was lost (treated as re-rasterizable vector)");
    }

    [Test]
    public void HighDensitySource_NotClampedByReducedOutputScale()
    {
        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            MosaicNode(),
            EffectiveScale.At(2),
            outputScale: 0.5f);

        Assert.That(measurement.HasFragments, Is.True);
        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(2).Within(1e-4),
            "a 2.0 source was clamped down by the 0.5 output scale — s_out must not cap an intermediate");
    }

    [TestCase(0.5f, 2.0f, 2.0f)]
    [TestCase(1.0f, 2.0f, 2.0f)]
    [TestCase(2.0f, 2.0f, 2.0f)]
    [TestCase(2.0f, 4.0f, 4.0f)]
    [TestCase(2.0f, 1.5f, 2.0f)]
    public void ConcreteAtSource_AtSupersampleOutput_ResolvesMaxOfSupplyAndOutput(
        float density,
        float outputScale,
        float expectedW)
    {
        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            MosaicNode(),
            EffectiveScale.At(density),
            outputScale);

        Assert.That(measurement.HasFragments, Is.True);
        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(expectedW).Within(1e-4),
            $"At({density}) @ outputScale {outputScale} resolved the wrong working scale");
        if (expectedW != 1)
        {
            Assert.That(measurement.EffectiveScale.IsUnbounded, Is.False,
                "a non-unit source density was lost at supersample output");
        }
    }

    [TestCase(float.PositiveInfinity, 4.0f)]
    [TestCase(2.0f, 2.0f)]
    public void MaxWorkingScale_CapsThroughTheNode(float maxWorkingScale, float expectedW)
    {
        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            MosaicNode(),
            EffectiveScale.At(4),
            outputScale: 1,
            maxWorkingScale);

        Assert.That(measurement.HasFragments, Is.True);
        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(expectedW).Within(1e-4),
            $"maxWorkingScale {maxWorkingScale} did not cap the working scale through the node");
    }

    [Test]
    public void At1Source_IsByteIdenticalToUnbounded_AtOutputScale1()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap at1 = RenderThroughMosaicAtScale1(EffectiveScale.At(1));
            using Bitmap unbounded = RenderThroughMosaicAtScale1(EffectiveScale.Unbounded);

            Assert.That(at1.GetPixelSpan().SequenceEqual(unbounded.GetPixelSpan()), Is.True,
                "At(1) source diverged from Unbounded at s_out == 1");
        });
    }

    private static Bitmap RenderThroughMosaicAtScale1(EffectiveScale sourceScale)
    {
        using var pipeline = ScaleRecordingTestHelper.Pipeline(
            ScaleRecordingTestHelper.Source(sourceScale),
            MosaicNode());
        using var renderer = new RenderNodeRenderer(
            pipeline,
            new RenderNodeRendererOptions { UseRenderCache = false });
        using RenderTarget target = RenderTarget.Create(120, 90)!;
        using (var canvas = new ImmediateCanvas(target, 1))
        {
            canvas.Clear(Colors.Black);
            renderer.Render(canvas);
        }

        return target.Snapshot();
    }

    [TestCase(0.5f, 2.0f, 4.0f)]
    [TestCase(2.0f, 2.0f, 1.0f)]
    [TestCase(1.0f, 2.0f, 2.0f)]
    [TestCase(0.25f, 1.0f, 4.0f)]
    public void TransformRenderNode_ScalesChildDensity_ByInverseScale(float scale, float density, float expected)
    {
        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            new TransformRenderNode(Matrix.CreateScale(scale, scale), TransformOperator.Prepend),
            EffectiveScale.At(density));

        Assert.That(measurement.EffectiveScale.IsUnbounded, Is.False);
        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(expected).Within(1e-4),
            $"Scale({scale}) on At({density}) must resolve to At({expected}) (density = px / logical unit)");
    }

    [Test]
    public void TransformRenderNode_AnisotropicScale_TakesDensestAxis()
    {
        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            new TransformRenderNode(Matrix.CreateScale(0.5f, 0.25f), TransformOperator.Prepend),
            EffectiveScale.At(1));

        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(4).Within(1e-4),
            "an anisotropic transform must project to the densest (most-shrunk) axis");
    }

    [Test]
    public void TransformRenderNode_PureRotation_LeavesDensityUnchanged()
    {
        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            new TransformRenderNode(Matrix.CreateRotation(MathF.PI / 4), TransformOperator.Prepend),
            EffectiveScale.At(2));

        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(2).Within(1e-4),
            "a pure rotation must not change the supply density");
    }

    [Test]
    public void TransformRenderNode_DegenerateScale_LeavesDensityUnchanged()
    {
        EffectiveScale zero = TransformRenderNode.RescaleDensity(
            EffectiveScale.At(2),
            Matrix.CreateScale(0, 0));
        Assert.That(zero.Value, Is.EqualTo(2).Within(1e-4));
        Assert.That(float.IsFinite(zero.Value), Is.True);

        EffectiveScale infinite = TransformRenderNode.RescaleDensity(
            EffectiveScale.At(2),
            Matrix.CreateScale(float.PositiveInfinity, float.PositiveInfinity));
        Assert.That(infinite.Value, Is.EqualTo(2).Within(1e-4),
            "an infinite transform scale must not collapse the density to At(0)");
    }

    [Test]
    public void TransformRenderNode_VectorChild_StaysUnbounded()
    {
        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            new TransformRenderNode(Matrix.CreateScale(0.5f, 0.5f), TransformOperator.Prepend),
            EffectiveScale.Unbounded);

        Assert.That(measurement.EffectiveScale.IsUnbounded, Is.True,
            "a vector child must stay Unbounded through a transform");
    }

    [Test]
    public void CustomTransformRenderNode_ScalesChildDensity_LikeTransformRenderNode()
    {
        Transform.Resource scale = new ScaleTransform(50, 50).ToResource(CompositionContext.Default);
        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            new DrawableGroup.CustomTransformRenderNode(
                scale,
                default,
                new Size(120, 90),
                AlignmentX.Left,
                AlignmentY.Top,
                new MemoryNode<Rect>(new Rect(0, 0, 120, 90))),
            EffectiveScale.At(2));

        Assert.That(measurement.EffectiveScale.IsUnbounded, Is.False,
            "the group/decorator transform dropped a concrete density to Unbounded");
        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(4).Within(1e-4),
            "CustomTransformRenderNode must rescale density by the inverse transform scale, like TransformRenderNode");
    }

    [Test]
    public void CustomTransformRenderNode_VectorChild_StaysUnbounded()
    {
        Transform.Resource scale = new ScaleTransform(50, 50).ToResource(CompositionContext.Default);
        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            new DrawableGroup.CustomTransformRenderNode(
                scale,
                default,
                new Size(10, 10),
                AlignmentX.Left,
                AlignmentY.Top,
                new MemoryNode<Rect>(new Rect(0, 0, 10, 10))),
            EffectiveScale.Unbounded);

        Assert.That(measurement.EffectiveScale.IsUnbounded, Is.True,
            "a vector child must stay Unbounded through the group/decorator transform");
    }

    [TestCase(0.5f, 1.0f)]
    [TestCase(1.0f, 1.0f)]
    [TestCase(2.0f, 2.0f)]
    public void CustomThenSkiaChain_ReportsConcreteDensity_NotUnbounded(float density, float expectedW)
    {
        var group = new FilterEffectGroup();
        var mosaic = new MosaicEffect();
        mosaic.TileSize.CurrentValue = new Size(10, 10);
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(3, 3);
        group.Children.Add(mosaic);
        group.Children.Add(blur);

        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            new FilterEffectRenderNode(group.ToResource(CompositionContext.Default)),
            EffectiveScale.At(density));

        Assert.That(measurement.HasFragments, Is.True, "the [Mosaic, Blur] chain dropped the input fragment");
        Assert.That(measurement.EffectiveScale.IsUnbounded, Is.False,
            "a flushed At(w) buffer behind a trailing Skia filter was over-reported as re-rasterizable Unbounded");
        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(expectedW).Within(1e-4),
            $"the [Mosaic, Blur] chain resolved the wrong working scale for At({density})");
    }

    [Test]
    public void StrokeEffect_OverBudgetBounds_ClampsWorkingScaleBelowNominal_DoesNotThrow()
    {
        var pen = new Pen();
        pen.Thickness.CurrentValue = 8;
        pen.Brush.CurrentValue = Brushes.Red;
        var stroke = new StrokeEffect();
        stroke.Pen.CurrentValue = pen;
        stroke.Offset.CurrentValue = new Point(20000, 0);

        RenderNodeMeasurement measurement = default;
        Assert.DoesNotThrow(() => measurement = ScaleRecordingTestHelper.MeasureThrough(
            new FilterEffectRenderNode(stroke.ToResource(CompositionContext.Default)),
            EffectiveScale.At(1)));

        Assert.That(measurement.HasFragments, Is.True, "the over-budget StrokeEffect dropped its fragment");
        Assert.That(measurement.EffectiveScale.Value, Is.GreaterThan(0));
        Assert.That(measurement.EffectiveScale.Value, Is.LessThan(1),
            "the over-budget stroke bounds did not clamp the working scale below the nominal 1.0");
    }

    private sealed class ClampToOutputRenderNode(FilterEffect.Resource effect) : FilterEffectRenderNode(effect)
    {
        private static readonly RenderScaleContract s_scale = RenderScaleContract.Custom(
            static metadata => MathF.Min(
                RenderScaleUtilities.ResolveWorkingScale(
                    metadata.InputSupplies.ToArray(),
                    metadata.OutputScale,
                    metadata.MaxWorkingScale),
                metadata.OutputScale),
            typeof(ClampToOutputRenderNode));

        protected override RenderScaleContract? GetWorkingScaleContract() => s_scale;
    }

    [TestCase(1.0f, 1.0f)]
    [TestCase(0.5f, 0.5f)]
    public void CustomRenderNode_OverridesSupplyDriven_WithClampToOutput(float outputScale, float expectedW)
    {
        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            new ClampToOutputRenderNode(new MosaicEffect().ToResource(CompositionContext.Default)),
            EffectiveScale.At(2),
            outputScale);

        Assert.That(measurement.HasFragments, Is.True);
        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(expectedW).Within(1e-4),
            "the custom render node did not apply its clamp-to-output working scale — the CreateRenderNode() escape hatch is broken");
    }

    [Test]
    public void CustomRenderNode_NoOpEffect_PassesInputThroughWithoutApplyingScaleContract()
    {
        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            new ClampToOutputRenderNode(new FilterEffectGroup().ToResource(CompositionContext.Default)),
            EffectiveScale.At(2));

        Assert.That(measurement.HasFragments, Is.True);
        Assert.That(measurement.EffectiveScale, Is.EqualTo(EffectiveScale.At(2)),
            "an effect that recorded no items must pass through the original input, not its custom working-scale map");
    }

    [Test]
    public void CustomWorkingScale_CanResolveBelowOutputScale()
    {
        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            new ConstantWorkingScaleRenderNode(
                CreateIdentityShaderEffect().ToResource(CompositionContext.Default),
                0.5f),
            EffectiveScale.At(1),
            outputScale: 1);

        Assert.That(measurement.EffectiveScale, Is.EqualTo(EffectiveScale.At(0.5f)),
            "an explicit Custom contract must not inherit the default output-scale floor");
    }

    [Test]
    public void CustomWorkingScalePolicy_InvokesResolverOncePerBranchWithBranchBounds()
    {
        Rect firstInputBounds = new(2, 3, 20, 10);
        Rect secondInputBounds = new(100, 200, 8, 6);
        Rect firstBufferBounds = new(1, 2, 22, 12);
        Rect secondBufferBounds = new(99, 199, 10, 8);
        s_scaleResolverObservations = [];
        try
        {
            var policy = new FilterEffectWorkingScalePolicy(RenderScaleContract.Custom(
                ObserveBranchWorkingScale,
                typeof(SourceEffectiveScaleFlowTests)));

            EffectiveScale resolved = policy.Resolve(
                [EffectiveScale.At(0.5f), EffectiveScale.At(2)],
                [firstInputBounds, secondInputBounds],
                [firstBufferBounds, secondBufferBounds],
                outputScale: 1,
                maxWorkingScale: 4);

            Assert.Multiple(() =>
            {
                Assert.That(resolved, Is.EqualTo(EffectiveScale.At(2)));
                Assert.That(s_scaleResolverObservations, Has.Count.EqualTo(2));
                Assert.That(
                    s_scaleResolverObservations.Select(static item => item.InputSupplies),
                    Is.All.Matches<IReadOnlyList<EffectiveScale>>(static supplies => supplies.Count == 1));
                Assert.That(
                    s_scaleResolverObservations.Select(static item => item.InputSupplies.Single()),
                    Is.EqualTo(new[] { EffectiveScale.At(0.5f), EffectiveScale.At(2) }));
                Assert.That(
                    s_scaleResolverObservations.Select(static item => item.OutputBounds),
                    Is.EqualTo(new[] { firstInputBounds, secondInputBounds }));
            });
        }
        finally
        {
            s_scaleResolverObservations = null;
        }
    }

    [Test]
    public void LegacyBufferBudgets_UseLocalOriginsAndIncludeIntermediateMaterializations()
    {
        Rect inputBounds = new(100, 20, 100, 10);
        var policy = new FilterEffectWorkingScalePolicy(RenderScaleContract.Custom(
            static _ => 2,
            "legacy-buffer-budget-test"));

        using var localOriginContext = new FilterEffectContext(inputBounds);
        localOriginContext.AppendSkiaFilter(
            0,
            static (_, input, _) => input,
            static (_, bounds) => bounds.WithWidth(bounds.Width + (bounds.X == 0 ? 20_000 : 0)));
        Rect[] localOriginFootprints = FilterEffectWorkingScalePolicy.CalculateLegacyBufferBounds(
                [inputBounds],
                localOriginContext.GetOrderedItems(),
                localOriginContext.Bounds);

        using var intermediateContext = new FilterEffectContext(inputBounds);
        intermediateContext.AppendSkiaFilter(
            0,
            static (_, input, _) => input,
            static (_, bounds) => bounds.WithWidth(20_000));
        intermediateContext.CustomEffect(
            0,
            static (_, _) => { },
            static (_, bounds) => bounds.WithWidth(100));
        Rect[] intermediateFootprints = FilterEffectWorkingScalePolicy.CalculateLegacyBufferBounds(
                [inputBounds],
                intermediateContext.GetOrderedItems(),
                intermediateContext.Bounds);

        Rect firstBounds = new(0, 0, 100, 10);
        Rect secondBounds = new(20_000, 0, 100, 10);
        Rect combinedBounds = firstBounds.Union(secondBounds);
        using var combinedContext = new FilterEffectContext(combinedBounds);
        combinedContext.CustomEffect(
            0,
            static (_, _) => { },
            static (_, bounds) => bounds);
        combinedContext.AppendSkiaFilter(
            0,
            static (_, input, _) => input,
            static (_, bounds) => bounds);
        Rect[] combinedFootprints = FilterEffectWorkingScalePolicy.CalculateLegacyBufferBounds(
            [firstBounds, secondBounds],
            combinedContext.GetOrderedItems(),
            combinedContext.Bounds);

        Rect nonlinearFirstBounds = new(0, 0, 100, 10);
        Rect nonlinearSecondBounds = new(100, 0, 100, 10);
        using var nonlinearContext = new FilterEffectContext(
            nonlinearFirstBounds.Union(nonlinearSecondBounds));
        nonlinearContext.AppendSkiaFilter(
            0,
            static (_, input, _) => input,
            static (_, bounds) => bounds.WithWidth(bounds.Width + bounds.X));
        nonlinearContext.CustomEffect(
            0,
            static (_, _) => { },
            static (_, bounds) => bounds);
        Rect[] nonlinearFootprints = FilterEffectWorkingScalePolicy.CalculateLegacyBufferBounds(
            [nonlinearFirstBounds, nonlinearSecondBounds],
            nonlinearContext.GetOrderedItems(),
            nonlinearContext.Bounds);

        Rect largeInputBounds = new(50, 10, 20_000, 10);
        using var immediateCustomContext = new FilterEffectContext(largeInputBounds);
        immediateCustomContext.CustomEffect(
            0,
            static (_, _) => { },
            static (_, bounds) => bounds.WithWidth(100));
        Rect[] immediateCustomFootprints = FilterEffectWorkingScalePolicy.CalculateLegacyBufferBounds(
            [largeInputBounds],
            immediateCustomContext.GetOrderedItems(),
            immediateCustomContext.Bounds);

        Rect fractionalInputBounds = new(100, 0, 8_192, 10);
        using var fractionalOriginContext = new FilterEffectContext(fractionalInputBounds);
        fractionalOriginContext.AppendSkiaFilter(
            0,
            static (_, input, _) => input,
            static (_, bounds) => bounds.X == 0 ? bounds.WithX(0.25f) : bounds);
        Rect[] fractionalOriginFootprints = FilterEffectWorkingScalePolicy.CalculateLegacyBufferBounds(
            [fractionalInputBounds],
            fractionalOriginContext.GetOrderedItems(),
            fractionalOriginContext.Bounds);
        EffectiveScale fractionalOriginScale = policy.Resolve(
            [EffectiveScale.At(1)],
            [fractionalInputBounds],
            fractionalOriginFootprints,
            outputScale: 1,
            maxWorkingScale: 4);

        Rect retainedBackingInputBounds = new(0, 0, RenderScaleUtilities.MaxBufferDimension, 1);
        Rect movedSemanticBounds = new(0.5f, 0, 1, 1);
        using var retainedBackingContext = new FilterEffectContext(retainedBackingInputBounds);
        retainedBackingContext.CustomEffect(
            0,
            static (_, _) => { },
            (_, _) => movedSemanticBounds);
        Rect[] retainedBackingFootprints = FilterEffectWorkingScalePolicy.CalculateLegacyBufferBounds(
            [retainedBackingInputBounds],
            retainedBackingContext.GetOrderedItems(),
            retainedBackingContext.Bounds);
        var unitScalePolicy = new FilterEffectWorkingScalePolicy(RenderScaleContract.Custom(
            static _ => 1,
            "retained-backing-budget-test"));
        EffectiveScale retainedBackingScale = unitScalePolicy.Resolve(
            [EffectiveScale.At(1)],
            [retainedBackingInputBounds],
            retainedBackingFootprints,
            outputScale: 1,
            maxWorkingScale: 4);

        Rect retainedThenInflatedInputBounds = new(
            0,
            0,
            RenderScaleUtilities.MaxBufferDimension - 4,
            1);
        Rect shrunkenSemanticBounds = new(0, 0, 1, 1);
        using var retainedThenInflatedContext = new FilterEffectContext(retainedThenInflatedInputBounds);
        retainedThenInflatedContext.CustomEffect(
            0,
            static (_, _) => { },
            (_, _) => shrunkenSemanticBounds);
        retainedThenInflatedContext.AppendSkiaFilter(
            0,
            static (_, input, _) => input,
            static (_, bounds) => bounds.Inflate(new Thickness(3)));
        Rect[] retainedThenInflatedFootprints = FilterEffectWorkingScalePolicy.CalculateLegacyBufferBounds(
            [retainedThenInflatedInputBounds],
            retainedThenInflatedContext.GetOrderedItems(),
            retainedThenInflatedContext.Bounds);
        EffectiveScale retainedThenInflatedScale = unitScalePolicy.Resolve(
            [EffectiveScale.At(1)],
            [retainedThenInflatedInputBounds],
            retainedThenInflatedFootprints,
            outputScale: 1,
            maxWorkingScale: 4);

        Rect negativeFractionalInputBounds = new(
            -0.25f,
            0,
            RenderScaleUtilities.MaxBufferDimension,
            1);
        Rect integerMovedSemanticBounds = new(0, 0, 1, 1);
        using var negativeFractionalContext = new FilterEffectContext(negativeFractionalInputBounds);
        negativeFractionalContext.CustomEffect(
            0,
            static (_, _) => { },
            (_, _) => integerMovedSemanticBounds);
        Rect[] negativeFractionalFootprints = FilterEffectWorkingScalePolicy.CalculateLegacyBufferBounds(
            [negativeFractionalInputBounds],
            negativeFractionalContext.GetOrderedItems(),
            negativeFractionalContext.Bounds);
        EffectiveScale negativeFractionalScale = unitScalePolicy.Resolve(
            [EffectiveScale.At(1)],
            [negativeFractionalInputBounds],
            negativeFractionalFootprints,
            outputScale: 1,
            maxWorkingScale: 4);

        EffectiveScale localOriginScale = policy.Resolve(
            [EffectiveScale.At(1)],
            [inputBounds],
            localOriginFootprints,
            outputScale: 1,
            maxWorkingScale: 4);
        EffectiveScale intermediateScale = policy.Resolve(
            [EffectiveScale.At(1)],
            [inputBounds],
            intermediateFootprints,
            outputScale: 1,
            maxWorkingScale: 4);

        Assert.Multiple(() =>
        {
            Assert.That(localOriginFootprints.Max(static bounds => bounds.Width), Is.EqualTo(20_100));
            Assert.That(localOriginScale.Value, Is.LessThan(1),
                "the runtime local-origin footprint must participate in the device-axis clamp");
            Assert.That(intermediateFootprints.Max(static bounds => bounds.Width), Is.EqualTo(20_000));
            Assert.That(intermediateFootprints, Has.Some.Matches<Rect>(static bounds => bounds.Width == 100));
            Assert.That(intermediateScale.Value, Is.LessThan(1),
                "a large pre-Custom Flush footprint must not be lost when the final bounds shrink");
            Assert.That(combinedFootprints.Max(static bounds => bounds.Width), Is.EqualTo(20_100),
                "an arbitrary Custom operation may combine branches, so later footprints must use aggregate bounds");
            Assert.That(nonlinearFootprints.Max(static bounds => bounds.Width), Is.EqualTo(300),
                "Custom collapse must union transformed branch results, not transform the original sparse union once");
            Assert.That(immediateCustomFootprints.Max(static bounds => bounds.Width), Is.EqualTo(20_000),
                "Custom performs a forced pre-callback Flush even without pending Skia work");
            Assert.That(fractionalOriginFootprints, Has.Some.Matches<Rect>(static bounds => bounds.X == 0.25));
            Assert.That(fractionalOriginScale.Value, Is.LessThan(2),
                "a transformed fractional allocation origin can add one device pixel at the axis limit");
            Assert.That(retainedBackingFootprints, Has.Some.Matches<Rect>(bounds =>
                    bounds.Position == movedSemanticBounds.Position
                    && bounds.Width == RenderScaleUtilities.MaxBufferDimension),
                "Custom may retain an input backing while moving or shrinking only its semantic bounds");
            Assert.That(retainedBackingScale.Value, Is.LessThan(1),
                "a retained axis-limit backing moved to a fractional origin must reserve its extra device pixel");
            Assert.That(retainedThenInflatedFootprints, Has.Some.Matches<Rect>(bounds =>
                    bounds.Width == RenderScaleUtilities.MaxBufferDimension + 2),
                "a Skia operation after Custom must transform the retained physical backing, not only semantics");
            Assert.That(retainedThenInflatedScale.Value, Is.LessThan(1),
                "the transformed retained backing must participate in the device-axis clamp");
            Assert.That(negativeFractionalFootprints, Has.Some.Matches<Rect>(static bounds =>
                    bounds.X == 0.25 && bounds.Width == RenderScaleUtilities.MaxBufferDimension),
                "reanchoring a retained backing must preserve its raster-to-semantic origin offset");
            Assert.That(negativeFractionalScale.Value, Is.LessThan(1),
                "a retained fractional raster offset can add one pixel after Custom repositions semantics");
        });
    }

    [Test]
    public void VectorWorkingScale_FallsBackToOutputScaleWhenEveryBranchIsUnbounded()
    {
        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            new VectorWorkingScaleRenderNode(
                CreateIdentityShaderEffect().ToResource(CompositionContext.Default)),
            EffectiveScale.Unbounded,
            outputScale: 1.5f);

        Assert.That(measurement.EffectiveScale, Is.EqualTo(EffectiveScale.At(1.5f)));
    }

    [Test]
    public void NoOpEffect_DoesNotEvaluateUnobservedWorkingScaleHookOrResolver()
    {
        s_throwingWorkingScaleResolverCalls = 0;
        using var node = new ThrowingWorkingScaleRenderNode(
            new FilterEffectGroup().ToResource(CompositionContext.Default));

        RenderNodeMeasurement measurement = default;
        Assert.DoesNotThrow(() => measurement = ScaleRecordingTestHelper.MeasureThrough(
            node,
            EffectiveScale.At(2)));

        Assert.Multiple(() =>
        {
            Assert.That(measurement.EffectiveScale, Is.EqualTo(EffectiveScale.At(2)));
            Assert.That(node.HookCalls, Is.Zero);
            Assert.That(s_throwingWorkingScaleResolverCalls, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NoOpEffect_DoesNotCommitFiniteOrOwningTargetIsolation(bool owningTargetDomain)
    {
        Rect bounds = new(2, 3, 12, 8);
        Rect targetDomain = new(0, 0, 24, 16);
        RenderPipelineDiagnosticSnapshot baseline = RasterizeWithDiagnostics(
            new TargetCommandSourceRenderNode(bounds, owningTargetDomain),
            targetDomain);
        var owned = new TrackingDisposable();
        var noOp = new WorkingScaleProbeEffect(context => _ = context.Own(owned));
        RenderPipelineDiagnosticSnapshot filtered = RasterizeWithDiagnostics(
            ScaleRecordingTestHelper.Pipeline(
                new TargetCommandSourceRenderNode(bounds, owningTargetDomain),
                new FilterEffectRenderNode(noOp.ToResource(CompositionContext.Default))),
            targetDomain);

        Assert.Multiple(() =>
        {
            foreach (RenderPipelineCounter counter in new[]
                     {
                         RenderPipelineCounter.RecordedFragments,
                         RenderPipelineCounter.RecordedLayers,
                         RenderPipelineCounter.PlannedGpuPasses,
                         RenderPipelineCounter.ExecutionIslands,
                         RenderPipelineCounter.OpaqueBoundaries,
                     })
            {
                Assert.That(filtered[counter], Is.EqualTo(baseline[counter]), counter.ToString());
            }
            Assert.That(owned.DisposeCount, Is.EqualTo(1),
                "a no-op effect must roll back resources that never enter the committed request");
        });
    }

    [Test]
    public void LegacyFilter_MaterializesUnboundedInputAtResolvedFragmentScale()
    {
        float observedWorkingScale = 0;
        PixelRect observedDeviceBounds = default;
        Rect bounds = new(3, 5, 12, 8);
        var effect = new WorkingScaleProbeEffect(context => context.Brightness(1));
        using var pipeline = ScaleRecordingTestHelper.Pipeline(
            ScaleRecordingTestHelper.Source(
                EffectiveScale.Unbounded,
                bounds,
                session =>
                {
                    observedWorkingScale = session.WorkingScale;
                    observedDeviceBounds = session.DeviceBounds;
                }),
            new ConstantWorkingScaleRenderNode(
                effect.ToResource(CompositionContext.Default),
                2));
        using var renderer = new RenderNodeRenderer(
            pipeline,
            new RenderNodeRendererOptions
            {
                UseRenderCache = false,
                TargetDomain = bounds,
                TargetFactory = new CpuTargetFactory(),
            });

        using RenderNodeRasterization result = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsEmpty, Is.False);
            Assert.That(observedWorkingScale, Is.EqualTo(2));
            Assert.That(observedDeviceBounds, Is.EqualTo(PixelRect.FromRect(bounds, 2)));
            Assert.That(observedDeviceBounds.Size, Is.EqualTo(new PixelSize(24, 16)));
        });
    }

    [Test]
    public void CustomWorkingScale_LegacyOperationPreservesSubOutputScale()
    {
        float observedWorkingScale = 0;
        PixelRect observedDeviceBounds = default;
        Rect bounds = new(3, 5, 12, 8);
        var effect = new WorkingScaleProbeEffect(context => context.Brightness(1));
        using var pipeline = ScaleRecordingTestHelper.Pipeline(
            ScaleRecordingTestHelper.Source(
                EffectiveScale.Unbounded,
                bounds,
                session =>
                {
                    observedWorkingScale = session.WorkingScale;
                    observedDeviceBounds = session.DeviceBounds;
                }),
            new ConstantWorkingScaleRenderNode(
                effect.ToResource(CompositionContext.Default),
                0.5f));
        using var renderer = new RenderNodeRenderer(
            pipeline,
            new RenderNodeRendererOptions
            {
                UseRenderCache = false,
                OutputScale = 1,
                TargetDomain = bounds,
                TargetFactory = new CpuTargetFactory(),
            });

        RenderNodeMeasurement measurement = renderer.Measure();
        using RenderNodeRasterization result = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsEmpty, Is.False);
            Assert.That(measurement.EffectiveScale, Is.EqualTo(EffectiveScale.At(0.5f)));
            Assert.That(observedWorkingScale, Is.EqualTo(0.5f));
            Assert.That(observedDeviceBounds, Is.EqualTo(PixelRect.FromRect(bounds, 0.5f)));
        });
    }

    [Test]
    public void LegacyFilter_PlannedScaleMatchesIntermediateFlushRuntimeScale()
    {
        float observedSourceScale = 0;
        Rect bounds = new(100, 20, 100, 10);
        Rect intermediateFootprint = new(0, 0, 20_000, 10);
        float widthOnlyClamp = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(
            intermediateFootprint,
            2);
        s_legacyCustomWorkingScale = 0;
        var effect = new WorkingScaleProbeEffect(context =>
        {
            context.AppendSkiaFilter(
                0,
                static (_, input, _) => SKImageFilter.CreateBlur(1, 1, input),
                static (_, current) => current.WithWidth(20_000));
            context.CustomEffect(
                0,
                RecordAndShrinkLegacyTargets,
                static (_, current) => current.WithWidth(100));
        });
        using var pipeline = ScaleRecordingTestHelper.Pipeline(
            ScaleRecordingTestHelper.Source(
                EffectiveScale.Unbounded,
                bounds,
                session => observedSourceScale = session.WorkingScale),
            new ConstantWorkingScaleRenderNode(
                effect.ToResource(CompositionContext.Default),
                2));
        using var renderer = new RenderNodeRenderer(
            pipeline,
            new RenderNodeRendererOptions
            {
                UseRenderCache = false,
                OutputScale = 1,
                TargetDomain = bounds,
                TargetFactory = new CpuTargetFactory(),
            });

        RenderNodeMeasurement measurement = renderer.Measure();
        using RenderNodeRasterization result = renderer.Rasterize();
        float plannedScale = measurement.EffectiveScale.Value;

        Assert.Multiple(() =>
        {
            Assert.That(plannedScale, Is.LessThanOrEqualTo(widthOnlyClamp));
            Assert.That(plannedScale, Is.LessThan(1), "the fixture must clamp below OutputScale");
            Assert.That(result.IsEmpty, Is.False);
            Assert.That(measurement.EffectiveScale.IsUnbounded, Is.False);
            Assert.That(observedSourceScale, Is.EqualTo(plannedScale));
            Assert.That(s_legacyCustomWorkingScale, Is.EqualTo(plannedScale));
        });
    }

    [Test]
    public void LegacyFilter_MultiInputClampUsesEachBufferInsteadOfSparseUnion()
    {
        Rect firstBounds = new(0, 0, 100, 100);
        Rect secondBounds = new(100_000, 0, 100, 100);
        var observedSources = new List<(float WorkingScale, PixelRect DeviceBounds)>();
        var effect = new WorkingScaleProbeEffect(context => context.Brightness(1));
        using var pipeline = ScaleRecordingTestHelper.MultiInputPipeline(
            [
                ScaleRecordingTestHelper.Source(
                    EffectiveScale.Unbounded,
                    firstBounds,
                    session => observedSources.Add((session.WorkingScale, session.DeviceBounds))),
                ScaleRecordingTestHelper.Source(
                    EffectiveScale.Unbounded,
                    secondBounds,
                    session => observedSources.Add((session.WorkingScale, session.DeviceBounds))),
            ],
            new ConstantWorkingScaleRenderNode(
                effect.ToResource(CompositionContext.Default),
                2));
        using var renderer = new RenderNodeRenderer(
            pipeline,
            new RenderNodeRendererOptions
            {
                UseRenderCache = false,
                RequestedRegion = firstBounds,
                MaxWorkingScale = 4,
                TargetFactory = new CpuTargetFactory(),
            });

        RenderNodeMeasurement measurement = renderer.Measure();
        using RenderNodeRasterization raster = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(raster.IsEmpty, Is.False);
            Assert.That(measurement.OutputBounds, Is.EqualTo(firstBounds.Union(secondBounds)));
            Assert.That(measurement.EffectiveScale, Is.EqualTo(EffectiveScale.At(2)),
                "the empty gap between independent buffers must not consume the dimension budget");
            Assert.That(observedSources, Has.Count.EqualTo(2));
            Assert.That(observedSources.Select(static item => item.WorkingScale), Is.All.EqualTo(2));
            Assert.That(
                observedSources.Select(static item => item.DeviceBounds),
                Is.EqualTo(new[]
                {
                    PixelRect.FromRect(firstBounds, 2),
                    PixelRect.FromRect(secondBounds, 2),
                }));
        });
    }

    [Test]
    public void CustomWorkingScale_NoOp_DoesNotRecordOrPlanAnExtraBoundary()
    {
        RenderPipelineDiagnosticSnapshot baseline = RasterizeWithDiagnostics(
            ScaleRecordingTestHelper.Source(EffectiveScale.At(2)));
        RenderPipelineDiagnosticSnapshot filtered = RasterizeWithDiagnostics(
            ScaleRecordingTestHelper.Pipeline(
                ScaleRecordingTestHelper.Source(EffectiveScale.At(2)),
                new ClampToOutputRenderNode(
                    new FilterEffectGroup().ToResource(CompositionContext.Default))));

        Assert.Multiple(() =>
        {
            Assert.That(
                filtered[RenderPipelineCounter.RecordedFragments],
                Is.EqualTo(baseline[RenderPipelineCounter.RecordedFragments]));
            Assert.That(
                filtered[RenderPipelineCounter.PlannedGpuPasses],
                Is.EqualTo(baseline[RenderPipelineCounter.PlannedGpuPasses]));
            Assert.That(
                filtered[RenderPipelineCounter.ExecutionIslands],
                Is.EqualTo(baseline[RenderPipelineCounter.ExecutionIslands]));
            Assert.That(
                filtered[RenderPipelineCounter.OpaqueBoundaries],
                Is.EqualTo(baseline[RenderPipelineCounter.OpaqueBoundaries]));
        });
    }

    [Test]
    public void CustomWorkingScale_CurrentPixelShader_DoesNotAddAnOpaqueMapPass()
    {
        var baselineEffect = CreateIdentityShaderEffect();
        RenderPipelineDiagnosticSnapshot baseline = RasterizeWithDiagnostics(
            ScaleRecordingTestHelper.Pipeline(
                ScaleRecordingTestHelper.Source(EffectiveScale.At(1)),
                new FilterEffectRenderNode(
                    baselineEffect.ToResource(CompositionContext.Default))));
        var effect = CreateIdentityShaderEffect();
        RenderPipelineDiagnosticSnapshot snapshot = RasterizeWithDiagnostics(
            ScaleRecordingTestHelper.Pipeline(
                ScaleRecordingTestHelper.Source(EffectiveScale.At(1)),
                new FixedWorkingScaleRenderNode(
                    effect.ToResource(CompositionContext.Default))));

        Assert.Multiple(() =>
        {
            Assert.That(snapshot[RenderPipelineCounter.RecordedFragments], Is.EqualTo(2));
            Assert.That(snapshot[RenderPipelineCounter.PlannedGpuPasses], Is.EqualTo(2));
            Assert.That(snapshot[RenderPipelineCounter.ExecutionIslands], Is.EqualTo(2));
            Assert.That(
                snapshot[RenderPipelineCounter.RecordedFragments],
                Is.EqualTo(baseline[RenderPipelineCounter.RecordedFragments]));
            Assert.That(
                snapshot[RenderPipelineCounter.PlannedGpuPasses],
                Is.EqualTo(baseline[RenderPipelineCounter.PlannedGpuPasses]));
            Assert.That(
                snapshot[RenderPipelineCounter.ExecutionIslands],
                Is.EqualTo(baseline[RenderPipelineCounter.ExecutionIslands]));
            Assert.That(
                snapshot[RenderPipelineCounter.OpaqueBoundaries],
                Is.EqualTo(baseline[RenderPipelineCounter.OpaqueBoundaries]),
                "the working-scale hook must not add an opaque identity-map boundary");
        });
    }

    [Test]
    public void CustomWorkingScale_CurrentPixelShader_ProducesDeclaredDensityAndDeviceFootprint()
    {
        EffectiveScale observedInputScale = default;
        PixelRect observedInputDeviceBounds = default;
        float observedWorkingScale = default;
        PixelRect observedOutputDeviceBounds = default;
        Rect bounds = new(3, 5, 12, 8);
        var effect = new WorkingScaleProbeEffect(context =>
        {
            context.Shader(ShaderDescription.CurrentPixel(
                "half4 apply(half4 color) { return color; }"));
            context.Geometry(GeometryDescription.Create(
                session =>
                {
                    observedInputScale = session.Input.EffectiveScale;
                    observedInputDeviceBounds = session.Input.DeviceBounds;
                    observedWorkingScale = session.WorkingScale;
                    observedOutputDeviceBounds = session.DeviceBounds;
                    session.Canvas.Use(session.Input.Draw);
                },
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput,
                structuralKey: "working-scale-device-footprint-probe",
                runtimeIdentity: new RenderRuntimeIdentity("working-scale-device-footprint-probe")));
        });
        using var pipeline = ScaleRecordingTestHelper.Pipeline(
            ScaleRecordingTestHelper.Source(EffectiveScale.At(1), bounds),
            new FixedWorkingScaleRenderNode(effect.ToResource(CompositionContext.Default)));
        using var renderer = new RenderNodeRenderer(
            pipeline,
            new RenderNodeRendererOptions
            {
                UseRenderCache = false,
                TargetFactory = new CpuTargetFactory(),
            });

        using RenderNodeRasterization result = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(result.IsEmpty, Is.False);
            Assert.That(observedInputScale, Is.EqualTo(EffectiveScale.At(2)));
            Assert.That(observedWorkingScale, Is.EqualTo(2));
            Assert.That(observedInputDeviceBounds, Is.EqualTo(PixelRect.FromRect(bounds, 2)));
            Assert.That(observedOutputDeviceBounds, Is.EqualTo(PixelRect.FromRect(bounds, 2)));
            Assert.That(observedInputDeviceBounds.Size, Is.EqualTo(new PixelSize(24, 16)));
        });
    }

    [Test]
    public void FusionPlanner_SplitsConcreteScaleTransitionsButPreservesSafeFusion()
    {
        using var mismatch = ScaleRecordingTestHelper.Pipeline(
            ScaleRecordingTestHelper.Source(EffectiveScale.At(1)),
            new ConstantWorkingScaleRenderNode(
                CreateIdentityShaderEffect().ToResource(CompositionContext.Default),
                2),
            new ConstantWorkingScaleRenderNode(
                CreateIdentityShaderEffect().ToResource(CompositionContext.Default),
                1));
        using CompiledRenderRequest mismatchRequest = Compile(mismatch);

        using var sameDensity = ScaleRecordingTestHelper.Pipeline(
            ScaleRecordingTestHelper.Source(EffectiveScale.At(1)),
            new ConstantWorkingScaleRenderNode(
                CreateIdentityShaderEffect().ToResource(CompositionContext.Default),
                2),
            new ConstantWorkingScaleRenderNode(
                CreateIdentityShaderEffect().ToResource(CompositionContext.Default),
                2));
        using CompiledRenderRequest sameDensityRequest = Compile(sameDensity);

        using var adoptedVector = ScaleRecordingTestHelper.Pipeline(
            ScaleRecordingTestHelper.Source(EffectiveScale.Unbounded),
            new DirectShaderRenderNode(),
            new ConstantWorkingScaleRenderNode(
                CreateIdentityShaderEffect().ToResource(CompositionContext.Default),
                2));
        using CompiledRenderRequest adoptedVectorRequest = Compile(adoptedVector);

        Assert.Multiple(() =>
        {
            Assert.That(mismatchRequest.ExecutionPlan.ShaderRuns.Count(), Is.EqualTo(2));
            Assert.That(mismatchRequest.ExecutionPlan.ShaderRuns, Has.All.Matches<CompiledShaderRun>(
                static run => run.Stages.Length == 1));
            Assert.That(mismatchRequest.ExecutionPlan.Boundaries, Has.Some.Matches<ExecutionIslandBoundary>(
                static boundary => boundary.Reason == ExecutionIslandBoundaryReason.ScaleTransition));
            Assert.That(sameDensityRequest.ExecutionPlan.ShaderRuns.Single().Stages, Has.Length.EqualTo(2));
            Assert.That(adoptedVectorRequest.ExecutionPlan.ShaderRuns.Single().Stages, Has.Length.EqualTo(2),
                "an Unbounded predecessor may adopt its concrete successor's density without a split");
        });
    }

    [Test]
    public void FusedShaderBinders_ObserveStageLocalScaleContext_WithDisabledParity()
    {
        Rect bounds = new(3, 5, 12, 8);
        var enabledObservations = new List<ShaderContextObservation>();
        using var enabledNode = ScaleRecordingTestHelper.Pipeline(
            ScaleRecordingTestHelper.Source(EffectiveScale.At(1), bounds),
            new ConstantWorkingScaleRenderNode(
                CreateContextProbeEffect(enabledObservations, "enabled-first")
                    .ToResource(CompositionContext.Default),
                2),
            new FilterEffectRenderNode(
                CreateContextProbeEffect(enabledObservations, "enabled-second")
                    .ToResource(CompositionContext.Default)));
        using var enabled = CreateCpuRenderer(enabledNode, bounds, FusionMode.Enabled);

        var disabledObservations = new List<ShaderContextObservation>();
        using var disabledNode = ScaleRecordingTestHelper.Pipeline(
            ScaleRecordingTestHelper.Source(EffectiveScale.At(1), bounds),
            new ConstantWorkingScaleRenderNode(
                CreateContextProbeEffect(disabledObservations, "disabled-first")
                    .ToResource(CompositionContext.Default),
                2),
            new FilterEffectRenderNode(
                CreateContextProbeEffect(disabledObservations, "disabled-second")
                    .ToResource(CompositionContext.Default)));
        using var disabled = CreateCpuRenderer(disabledNode, bounds, FusionMode.Disabled);

        using RenderNodeRasterization enabledRaster = enabled.Rasterize();
        using RenderNodeRasterization disabledRaster = disabled.Rasterize();

        Assert.That(enabledRaster.Bitmap, Is.Not.Null);
        Assert.That(disabledRaster.Bitmap, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(enabled.LastExecutionStatistics.ShaderRunExecutions, Is.EqualTo(1));
            Assert.That(enabled.LastExecutionStatistics.FusedShaderRunExecutions, Is.EqualTo(1));
            Assert.That(disabled.LastExecutionStatistics.ShaderRunExecutions, Is.EqualTo(2));
            Assert.That(disabled.LastExecutionStatistics.FusedShaderRunExecutions, Is.Zero);
            Assert.That(enabledObservations, Is.EqualTo(disabledObservations));
            Assert.That(
                enabledObservations.Select(static item => item.InputEffectiveScale),
                Is.EqualTo(new[] { EffectiveScale.At(1), EffectiveScale.At(2) }));
            Assert.That(
                enabledObservations.Select(static item => item.WorkingScale),
                Is.EqualTo(new[] { 2f, 2f }));
            Assert.That(
                enabledObservations.Select(static item => item.DeviceBounds),
                Is.All.EqualTo(PixelRect.FromRect(bounds, 2)));
            Assert.That(
                enabledRaster.Bitmap!.GetPixelSpan().SequenceEqual(disabledRaster.Bitmap!.GetPixelSpan()),
                Is.True);
        });
    }

    [Test]
    public void FusedShaderBinders_ObserveRuntimeClampedScaleContext_WithDisabledParity()
    {
        Rect bounds = new(0, 0, 10_000, 1);
        EffectiveScale sourceScale = EffectiveScale.At(2);
        PixelRect sourceDeviceBounds = PixelRect.FromRect(bounds, sourceScale.Value);
        float clampedScale = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(bounds, sourceScale.Value);
        PixelRect clampedDeviceBounds = PixelRect.FromRect(bounds, clampedScale);
        using RenderTarget source = new CpuRenderTarget(sourceDeviceBounds.Width, sourceDeviceBounds.Height);

        var enabledObservations = new List<ShaderContextObservation>();
        using var enabledNode = ScaleRecordingTestHelper.Pipeline(
            new MaterializedSourceRenderNode(source, bounds, sourceScale),
            new DirectShaderRenderNode(
                CreateContextProbeShaderDescription(enabledObservations, "enabled-clamped-first")),
            new DirectShaderRenderNode(
                CreateContextProbeShaderDescription(enabledObservations, "enabled-clamped-second")));
        using var enabled = CreateCpuRenderer(enabledNode, bounds, FusionMode.Enabled);

        var disabledObservations = new List<ShaderContextObservation>();
        using var disabledNode = ScaleRecordingTestHelper.Pipeline(
            new MaterializedSourceRenderNode(source, bounds, sourceScale),
            new DirectShaderRenderNode(
                CreateContextProbeShaderDescription(disabledObservations, "disabled-clamped-first")),
            new DirectShaderRenderNode(
                CreateContextProbeShaderDescription(disabledObservations, "disabled-clamped-second")));
        using var disabled = CreateCpuRenderer(disabledNode, bounds, FusionMode.Disabled);

        using RenderNodeRasterization enabledRaster = enabled.Rasterize();
        using RenderNodeRasterization disabledRaster = disabled.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(clampedScale, Is.LessThan(sourceScale.Value),
                "the fixture must exceed the per-buffer device-axis limit");
            Assert.That(enabled.LastExecutionStatistics.ShaderRunExecutions, Is.EqualTo(1));
            Assert.That(enabled.LastExecutionStatistics.FusedShaderRunExecutions, Is.EqualTo(1));
            Assert.That(disabled.LastExecutionStatistics.ShaderRunExecutions, Is.EqualTo(2));
            Assert.That(disabled.LastExecutionStatistics.FusedShaderRunExecutions, Is.Zero);
            Assert.That(enabledObservations, Is.EqualTo(disabledObservations));
            Assert.That(
                enabledObservations.Select(static item => item.InputEffectiveScale),
                Is.EqualTo(new[] { sourceScale, EffectiveScale.At(clampedScale) }));
            Assert.That(
                enabledObservations.Select(static item => item.WorkingScale),
                Is.All.EqualTo(clampedScale));
            Assert.That(
                enabledObservations.Select(static item => item.DeviceBounds),
                Is.All.EqualTo(clampedDeviceBounds));
            Assert.That(
                enabledRaster.Bitmap!.GetPixelSpan().SequenceEqual(disabledRaster.Bitmap!.GetPixelSpan()),
                Is.True);
        });
    }

    [Test]
    public void StructuralPlanCache_RecompilesWhenScaleCompatibilityChanges()
    {
        Rect bounds = new(0, 0, 12, 8);
        using var node = new MutableScaleFusionNode(bounds, density: 1);
        using var renderer = CreateCpuRenderer(node, bounds, FusionMode.Enabled);

        using (renderer.Rasterize())
        {
            Assert.That(renderer.LastExecutionStatistics.FusedShaderRunExecutions, Is.EqualTo(1));
        }

        node.UpdateDensity(2);
        using (renderer.Rasterize())
        {
            Assert.That(renderer.LastExecutionStatistics.ShaderRunExecutions, Is.EqualTo(2));
            Assert.That(renderer.LastExecutionStatistics.FusedShaderRunExecutions, Is.Zero);
        }

        Assert.Multiple(() =>
        {
            Assert.That(renderer.StructuralPlanCacheStatistics.Compilations, Is.EqualTo(2));
            Assert.That(renderer.StructuralPlanCacheStatistics.Misses, Is.EqualTo(2));
            Assert.That(renderer.StructuralPlanCacheStatistics.Replacements, Is.EqualTo(1));
            Assert.That(renderer.StructuralPlanCacheStatistics.Hits, Is.Zero);
        });
    }

    private static RenderPipelineDiagnosticSnapshot RasterizeWithDiagnostics(
        RenderNode root,
        Rect? targetDomain = null)
    {
        var diagnostics = new RenderPipelineDiagnosticsState();
        using (root)
        using (var renderer = new RenderNodeRenderer(
                   root,
                   new RenderNodeRendererOptions
                   {
                       UseRenderCache = false,
                       TargetDomain = targetDomain,
                       Diagnostics = diagnostics,
                       TargetFactory = new CpuTargetFactory(),
                   }))
        using (renderer.Rasterize())
        {
        }

        return diagnostics.Latest;
    }

    private static WorkingScaleProbeEffect CreateIdentityShaderEffect()
        => new(static context => context.Shader(ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return color; }")));

    private static WorkingScaleProbeEffect CreateContextProbeEffect(
        ICollection<ShaderContextObservation> observations,
        string identity)
        => new(context => context.Shader(CreateContextProbeShaderDescription(observations, identity)));

    private static ShaderDescription CreateContextProbeShaderDescription(
        ICollection<ShaderContextObservation> observations,
        string identity)
        => ShaderDescription.CurrentPixel(
            "uniform float gain; half4 apply(half4 color) { return color * gain; }",
            bindings => bindings.Uniform(
                "gain",
                1f,
                (writer, _, execution) =>
                {
                    observations.Add(ShaderContextObservation.Capture(execution));
                    writer.Set(1f);
                },
                structuralKey: identity,
                runtimeIdentity: new RenderRuntimeIdentity(identity)));

    private static RenderNodeRenderer CreateCpuRenderer(
        RenderNode node,
        Rect targetDomain,
        FusionMode fusionMode)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                UseRenderCache = false,
                OutputScale = 1,
                MaxWorkingScale = 4,
                TargetDomain = targetDomain,
                FusionMode = fusionMode,
                TargetFactory = new CpuTargetFactory(),
            });

    private static CompiledRenderRequest Compile(RenderNode node)
    {
        var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            maxWorkingScale: 4,
            fusionMode: FusionMode.Enabled));
        try
        {
            RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
            return new RenderRequestCompiler().Compile(request, graph);
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private sealed class FixedWorkingScaleRenderNode(FilterEffect.Resource effect)
        : FilterEffectRenderNode(effect)
    {
        private static readonly RenderScaleContract s_scale = RenderScaleContract.Custom(
            static _ => 2,
            typeof(FixedWorkingScaleRenderNode));

        protected override RenderScaleContract? GetWorkingScaleContract() => s_scale;
    }

    private sealed class ConstantWorkingScaleRenderNode : FilterEffectRenderNode
    {
        private readonly RenderScaleContract _scale;

        public ConstantWorkingScaleRenderNode(FilterEffect.Resource effect, float scale)
            : base(effect)
        {
            _scale = RenderScaleContract.Custom(
                new ConstantWorkingScaleResolver(scale).Resolve,
                new ConstantWorkingScaleIdentity(scale));
        }

        protected override RenderScaleContract? GetWorkingScaleContract() => _scale;
    }

    private sealed class VectorWorkingScaleRenderNode(FilterEffect.Resource effect)
        : FilterEffectRenderNode(effect)
    {
        protected override RenderScaleContract? GetWorkingScaleContract() => RenderScaleContract.Vector;
    }

    private sealed class PreserveWorkingScaleRenderNode(FilterEffect.Resource effect)
        : FilterEffectRenderNode(effect)
    {
        protected override RenderScaleContract? GetWorkingScaleContract()
            => RenderScaleContract.PreserveInputSupply;
    }

    private sealed class ThrowingWorkingScaleRenderNode(FilterEffect.Resource effect)
        : FilterEffectRenderNode(effect)
    {
        private static readonly RenderScaleContract s_scale = RenderScaleContract.Custom(
            ThrowWorkingScaleResolver,
            typeof(ThrowingWorkingScaleRenderNode));

        public int HookCalls { get; private set; }

        protected override RenderScaleContract? GetWorkingScaleContract()
        {
            HookCalls++;
            return s_scale;
        }
    }

    private sealed class TargetCommandSourceRenderNode(Rect bounds, bool owningTargetDomain) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            TargetRegion region = owningTargetDomain
                ? TargetRegion.Full
                : TargetRegion.Region(bounds);
            context.Publish(context.TargetCommand([], TargetCommandDescription.Create(
                static session => session.Canvas.Use(static _ => { }),
                region,
                bounds,
                RenderHitTestContract.None,
                TargetAccess.ReadWrite,
                structuralKey: new TargetCommandSourceIdentity(bounds, owningTargetDomain),
                runtimeIdentity: new RenderRuntimeIdentity(
                    new TargetCommandSourceIdentity(bounds, owningTargetDomain)))));
        }
    }

    private sealed class DirectShaderRenderNode(ShaderDescription? description = null) : RenderNode
    {
        private static readonly ShaderDescription s_shader = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return color; }");

        private readonly ShaderDescription _description = description ?? s_shader;

        public override void Process(RenderNodeContext context)
        {
            foreach (RenderFragmentHandle input in context.Inputs)
                context.Publish(context.Shader(input, _description));
        }
    }

    private sealed class MaterializedSourceRenderNode(
        RenderTarget source,
        Rect bounds,
        EffectiveScale scale) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            var identity = new MaterializedSourceIdentity(bounds, scale);
            RenderResource<RenderTarget> target = context.Borrow(source, identity, version: 1);
            context.Publish(context.MaterializedInput(MaterializedInputDescription.FromRenderTarget(
                target,
                bounds,
                scale,
                RenderHitTestContract.OutputBounds)));
        }
    }

    private sealed class MutableScaleFusionNode : RenderNode
    {
        private readonly Rect _bounds;
        private readonly PreserveWorkingScaleRenderNode _preserve;
        private readonly ConstantWorkingScaleRenderNode _fixed;
        private float _density;

        public MutableScaleFusionNode(Rect bounds, float density)
        {
            _bounds = bounds;
            _density = density;
            _preserve = new PreserveWorkingScaleRenderNode(
                CreateIdentityShaderEffect().ToResource(CompositionContext.Default));
            _fixed = new ConstantWorkingScaleRenderNode(
                CreateIdentityShaderEffect().ToResource(CompositionContext.Default),
                1);
        }

        public void UpdateDensity(float density)
        {
            _density = density;
            HasChanges = true;
        }

        public override void Process(RenderNodeContext context)
        {
            Brush.Resource fill = Brushes.Resource.White;
            RenderResource<Brush.Resource> fillToken = context.Borrow(
                fill,
                fill.GetOriginal().Id,
                fill.Version);
            float density = _density;
            OpaqueRenderDescription source = OpaqueRenderDescription.Create(
                session => session.UseResource(fillToken, currentFill =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(_bounds);
                    output.Canvas.Use(canvas => canvas.DrawRectangle(_bounds, currentFill, null));
                    session.Publish(output);
                }),
                RenderOperationBoundsContract.Source(_bounds),
                RenderHitTestContract.None,
                RenderValueCardinality.Single,
                RenderScaleContract.Custom(
                    new ConstantWorkingScaleResolver(density).Resolve,
                    "mutable-scale-contract"),
                structuralKey: "mutable-scale-source",
                runtimeIdentity: new RenderRuntimeIdentity(
                    new MutableScaleSourceRuntimeIdentity(density)),
                resources: [fillToken]);
            RenderFragmentHandle current = context.OpaqueSource(source);
            current = context.RecordNode(_preserve, [current]).Single();
            current = context.RecordNode(_fixed, [current]).Single();
            context.Publish(current);
        }

        protected override void OnDispose(bool disposing)
        {
            _fixed.Dispose();
            _preserve.Dispose();
            base.OnDispose(disposing);
        }
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }

    private static float ThrowWorkingScaleResolver(RenderScaleContext _)
    {
        s_throwingWorkingScaleResolverCalls++;
        throw new InvalidOperationException("The no-op resolver must remain lazy.");
    }

    private static void RecordAndShrinkLegacyTargets(int _, CustomFilterEffectContext context)
    {
        s_legacyCustomWorkingScale = context.WorkingScale;
        for (int index = 0; index < context.Targets.Count; index++)
        {
            EffectTarget current = context.Targets[index];
            EffectTarget replacement = context.CreateTarget(current.Bounds.WithWidth(100));
            using (ImmediateCanvas canvas = context.Open(replacement))
                canvas.Clear();
            current.Dispose();
            context.Targets[index] = replacement;
        }
    }

    private static float ObserveBranchWorkingScale(RenderScaleContext context)
    {
        s_scaleResolverObservations!.Add(new ScaleResolverObservation(
            context.InputSupplies.ToArray(),
            context.OutputBounds));
        EffectiveScale supply = context.InputSupplies.Single();
        return supply.IsUnbounded ? context.OutputScale : supply.Value;
    }

    private sealed record ConstantWorkingScaleResolver(float Value)
    {
        public float Resolve(RenderScaleContext _) => Value;
    }

    private readonly record struct ShaderContextObservation(
        Rect InputBounds,
        Rect OutputBounds,
        Rect RequiredRegion,
        PixelRect DeviceBounds,
        EffectiveScale InputEffectiveScale,
        float OutputScale,
        float WorkingScale,
        float MaxWorkingScale)
    {
        public static ShaderContextObservation Capture(ShaderExecutionContext context)
            => new(
                context.InputBounds,
                context.OutputBounds,
                context.RequiredRegion,
                context.DeviceBounds,
                context.InputEffectiveScale,
                context.OutputScale,
                context.WorkingScale,
                context.MaxWorkingScale);
    }

    private readonly record struct ScaleResolverObservation(
        IReadOnlyList<EffectiveScale> InputSupplies,
        Rect OutputBounds);

    private readonly record struct ConstantWorkingScaleIdentity(float Value);

    private readonly record struct TargetCommandSourceIdentity(Rect Bounds, bool OwningTargetDomain);

    private readonly record struct MutableScaleSourceRuntimeIdentity(float Density);

    private readonly record struct MaterializedSourceIdentity(Rect Bounds, EffectiveScale Scale);

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(PixelSize deviceSize)
            => new CpuRenderTarget(deviceSize.Width, deviceSize.Height);
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);

    private sealed class OversampleMosaicRenderNode(FilterEffect.Resource effect) : FilterEffectRenderNode(effect)
    {
        private static readonly RenderScaleContract s_scale = RenderScaleContract.Custom(
            static metadata => MathF.Min(
                MathF.Max(
                    RenderScaleUtilities.ResolveWorkingScale(
                        metadata.InputSupplies.ToArray(),
                        metadata.OutputScale,
                        metadata.MaxWorkingScale),
                    2 * metadata.OutputScale),
                metadata.MaxWorkingScale),
            typeof(OversampleMosaicRenderNode));

        protected override RenderScaleContract? GetWorkingScaleContract() => s_scale;
    }

    [Test]
    public void OversampleMosaicRenderNode_RunsRealEffect_AboveSupply_AtTwiceOutputScale()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var mosaic = new MosaicEffect();
            mosaic.TileSize.CurrentValue = new Size(10, 10);
            using var pipeline = ScaleRecordingTestHelper.Pipeline(
                ScaleRecordingTestHelper.Source(EffectiveScale.At(1)),
                new OversampleMosaicRenderNode(mosaic.ToResource(CompositionContext.Default)));
            using var renderer = new RenderNodeRenderer(
                pipeline,
                new RenderNodeRendererOptions { UseRenderCache = false });

            RenderNodeMeasurement measurement = renderer.Measure();
            Assert.That(measurement.HasFragments, Is.True,
                "the oversample escape hatch dropped its fragment — the effect did not apply");
            Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(2).Within(1e-4),
                "the oversample hatch must run the effect at w = 2 * s_out");
            Assert.That(measurement.EffectiveScale.IsUnbounded, Is.False,
                "an oversampled effect buffer must be concrete At(w), not Unbounded");

            using RenderTarget target = RenderTarget.Create(120, 90)!;
            using (var canvas = new ImmediateCanvas(target, 1))
            {
                canvas.Clear(Colors.Black);
                renderer.Render(canvas);
            }

            using Bitmap snapshot = target.Snapshot();
            using RenderTarget blackTarget = RenderTarget.Create(120, 90)!;
            using (var blackCanvas = new ImmediateCanvas(blackTarget))
                blackCanvas.Clear(Colors.Black);
            using Bitmap black = blackTarget.Snapshot();
            Assert.That(ImageMetrics.MeanAbsoluteError(snapshot, black), Is.GreaterThan(0.01),
                "the oversampled effect produced an all-black buffer (it silently failed to draw)");
        });
    }

    [Test]
    public void Push_RoutesThroughOverriddenCreateRenderNode_AndCustomWorkingScaleApplies()
    {
        var effect = new ClampToOutputEffect();
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var container = new ContainerRenderNode();
        using var context = new GraphicsContext2D(container, new Size(120, 90), outputScale: 1);

        using (resource.Push(context))
        {
        }

        Assert.That(container.Children, Has.Count.EqualTo(1), "Push added no render node");
        Assert.That(container.Children[0], Is.TypeOf<ClampToOutputEscapeHatchNode>(),
            "Push bypassed the overridden CreateRenderNode() escape hatch (FR-036)");

        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.MeasureThrough(
            container.Children[0],
            EffectiveScale.At(2));
        Assert.That(measurement.HasFragments, Is.True);
        Assert.That(measurement.EffectiveScale.Value, Is.EqualTo(1).Within(1e-4),
            "the overridden render node's clamp-to-output working scale did not drive the pipeline end-to-end");
    }
}

[SuppressResourceClassGeneration]
internal sealed partial class ClampToOutputEffect : FilterEffect
{
    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        context.Brightness(1);
    }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource
    {
        public override FilterEffectRenderNode CreateRenderNode() => new ClampToOutputEscapeHatchNode(this);
    }
}

internal sealed class ClampToOutputEscapeHatchNode(FilterEffect.Resource effect) : FilterEffectRenderNode(effect)
{
    private static readonly RenderScaleContract s_scale = RenderScaleContract.Custom(
        static metadata => MathF.Min(
            RenderScaleUtilities.ResolveWorkingScale(
                metadata.InputSupplies.ToArray(),
                metadata.OutputScale,
                metadata.MaxWorkingScale),
            metadata.OutputScale),
        typeof(ClampToOutputEscapeHatchNode));

    protected override RenderScaleContract? GetWorkingScaleContract() => s_scale;
}

[SuppressResourceClassGeneration]
internal sealed partial class WorkingScaleProbeEffect(Action<FilterEffectContext> apply) : FilterEffect
{
    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        => apply(context);

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = false;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource;
}

internal static class ScaleRecordingTestHelper
{
    private static readonly Rect s_defaultBounds = new(0, 0, 120, 90);

    public static RenderNode Source(
        EffectiveScale scale,
        Rect? bounds = null,
        Action<OpaqueRenderSession>? observe = null)
        => new FixedScaleSourceRenderNode(bounds ?? s_defaultBounds, scale, observe);

    public static RenderNode Materialize()
        => new MaterializeInputsRenderNode();

    public static RenderNode Layer(Rect domain)
        => new FiniteLayerInputsRenderNode(domain);

    public static RecordingPipelineRenderNode Pipeline(params RenderNode[] stages)
        => new(stages);

    public static RenderNode MultiInputPipeline(RenderNode[] sources, RenderNode stage)
        => new MultiInputRecordingPipelineRenderNode(sources, stage);

    public static RecordingSubtreePipelineRenderNode SubtreePipeline(
        RenderNode subtree,
        params RenderNode[] stages)
        => new(subtree, stages);

    public static RenderNodeMeasurement MeasureThrough(
        RenderNode node,
        EffectiveScale sourceScale,
        float outputScale = 1,
        float maxWorkingScale = float.PositiveInfinity)
    {
        using var pipeline = Pipeline(Source(sourceScale), node);
        return Measure(pipeline, outputScale, maxWorkingScale);
    }

    public static RenderNodeMeasurement Measure(
        RenderNode root,
        float outputScale = 1,
        float maxWorkingScale = float.PositiveInfinity,
        Rect? targetDomain = null)
    {
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                OutputScale = outputScale,
                MaxWorkingScale = maxWorkingScale,
                TargetDomain = targetDomain,
                UseRenderCache = false,
            });
        return renderer.Measure();
    }

    internal sealed class RecordingPipelineRenderNode(params RenderNode[] stages) : RenderNode
    {
        private readonly RenderNode[] _stages = stages;

        public override void Process(RenderNodeContext context)
        {
            IReadOnlyList<RenderFragmentHandle> inputs = [];
            foreach (RenderNode stage in _stages)
                inputs = context.RecordNode(stage, inputs);

            context.PublishRange(inputs);
        }

        protected override void OnDispose(bool disposing)
        {
            foreach (RenderNode stage in _stages)
                stage.Dispose();
        }
    }

    internal sealed class RecordingSubtreePipelineRenderNode(
        RenderNode subtree,
        params RenderNode[] stages) : RenderNode
    {
        private readonly RenderNode _subtree = subtree;
        private readonly RenderNode[] _stages = stages;

        public override void Process(RenderNodeContext context)
        {
            IReadOnlyList<RenderFragmentHandle> inputs = context.RecordSubtree(_subtree);
            foreach (RenderNode stage in _stages)
                inputs = context.RecordNode(stage, inputs);

            context.PublishRange(inputs);
        }

        protected override void OnDispose(bool disposing)
        {
            _subtree.Dispose();
            foreach (RenderNode stage in _stages)
                stage.Dispose();
        }
    }

    private sealed class MultiInputRecordingPipelineRenderNode(
        RenderNode[] sources,
        RenderNode stage) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle[] inputs = sources
                .SelectMany(source => context.RecordNode(source, []))
                .ToArray();
            context.PublishRange(context.RecordNode(stage, inputs));
        }

        protected override void OnDispose(bool disposing)
        {
            stage.Dispose();
            foreach (RenderNode source in sources)
                source.Dispose();
            base.OnDispose(disposing);
        }
    }

    private sealed class FixedScaleSourceRenderNode(
        Rect bounds,
        EffectiveScale scale,
        Action<OpaqueRenderSession>? observe) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            Brush.Resource fill = Brushes.Resource.White;
            RenderResource<Brush.Resource> fillToken = context.Borrow(
                fill,
                fill.GetOriginal().Id,
                fill.Version);
            RenderScaleContract scaleContract = scale.IsUnbounded
                ? RenderScaleContract.Vector
                : RenderScaleContract.Custom(
                    new FixedScaleResolver(scale.Value).Resolve,
                    new FixedScaleIdentity(scale.Value));
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                execute: session => session.UseResource(fillToken, currentFill =>
                {
                    observe?.Invoke(session);
                    using OpaqueRenderOutput output = session.CreateOutput(bounds);
                    output.Canvas.Use(canvas => canvas.DrawRectangle(bounds, currentFill, null));
                    session.Publish(output);
                }),
                bounds: RenderOperationBoundsContract.Source(bounds),
                hitTest: RenderHitTestContract.None,
                valueCardinality: RenderValueCardinality.Single,
                scale: scaleContract,
                structuralKey: new FixedScaleSourceIdentity(bounds, scale),
                runtimeIdentity: new RenderRuntimeIdentity(new FixedScaleSourceIdentity(bounds, scale)),
                resources: [fillToken]);
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class MaterializeInputsRenderNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            foreach (RenderFragmentHandle input in context.Inputs)
            {
                OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                    execute: static session =>
                    {
                        using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                        output.Canvas.Use(session.Inputs[0].Draw);
                        session.Publish(output);
                    },
                    bounds: RenderOperationBoundsContract.Map(RenderBoundsContract.Identity),
                    hitTest: RenderHitTestContract.AnyInput,
                    valueCardinality: RenderValueCardinality.Single,
                    scale: RenderScaleContract.MaterializeAtWorkingScale,
                    structuralKey: typeof(MaterializeInputsRenderNode));
                context.Publish(context.OpaqueMap(input, description));
            }
        }
    }

    private sealed class FiniteLayerInputsRenderNode(Rect domain) : RenderNode
    {
        public override void Process(RenderNodeContext context)
            => context.Publish(context.Layer(context.Inputs, domain));
    }

    private sealed record FixedScaleResolver(float Value)
    {
        public float Resolve(RenderScaleContext _) => Value;
    }

    private readonly record struct FixedScaleIdentity(float Value);

    private readonly record struct FixedScaleSourceIdentity(Rect Bounds, EffectiveScale Scale);
}
