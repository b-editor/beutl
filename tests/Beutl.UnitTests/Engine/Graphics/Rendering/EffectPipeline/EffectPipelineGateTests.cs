using System.Linq.Expressions;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// The remaining structural gates for feature 004 step 5b (T050): FR-007 peak-intermediates (a longer chain never
/// raises the peak of concurrently live pooled intermediates above a shorter same-shape chain — the double-buffer
/// bound, measured by executing both chains against a pool and reading its live-lease high-water mark, then
/// cross-checked against the <see cref="ResourcePlan"/> declared bound) and FR-014 no-Vulkan safety (fused /
/// Skia-filter plans run entirely on the Skia backend, so a raster/Skia-only context executes them; a ComputeNode
/// is the only Vulkan-backed pass and always declares a fallback). Everything runs raster, so no GPU is needed.
/// </summary>
[TestFixture]
public class EffectPipelineGateTests
{
    private static readonly Rect s_bounds = new(0, 0, 128, 96);

    private static EffectGraphBuilder NewBuilder()
        => new(s_bounds, outputScale: 1f, workingScale: 1f, RenderIntent.Delivery);

    private static CompiledPlan Compile(EffectGraphBuilder builder)
    {
        using EffectGraph graph = builder.Build();
        return EffectGraphCompiler.Compile(graph, diagnostics: null);
    }

    // Alternates a Skia filter and a fused color op so the N effects compile to N distinct, non-grouping passes
    // (adjacent Skia filters would otherwise group; a fused color run breaks the run).
    private static EffectGraphBuilder AlternatingChain(int effects)
    {
        EffectGraphBuilder builder = NewBuilder();
        for (int i = 0; i < effects; i++)
        {
            if (i % 2 == 0)
                builder.Dilate(1, 1);
            else
                builder.Saturate(1.1f);
        }

        return builder;
    }

    // FR-007, measured: the declared plan bound alone would be tautological (a linear schedule always declares
    // [i, i+1] lifetimes), so this executes both chains against a real pool and compares the pool's live-lease
    // high-water marks — the number of intermediates that were genuinely allocated concurrently.
    [Test]
    public void TenEffectChain_MeasuredPeakLiveNotAboveThreeEffectChain()
    {
        (long measured3, CompiledPlan three) = ExecuteAndMeasurePeak(AlternatingChain(3));
        (long measured10, CompiledPlan ten) = ExecuteAndMeasurePeak(AlternatingChain(10));

        Assert.Multiple(() =>
        {
            Assert.That(ten.Passes.Length, Is.GreaterThan(three.Passes.Length),
                "the 10-effect chain really is a longer schedule");
            Assert.That(measured10, Is.GreaterThan(0), "the chain really acquired pooled intermediates");
            Assert.That(measured10, Is.LessThanOrEqualTo(measured3),
                "FR-007: a longer chain never holds more concurrently live intermediates than a shorter same-shape one");
            Assert.That(measured3, Is.LessThanOrEqualTo(three.Resources.PeakLiveCount),
                "the measured peak stays within the plan's declared bound");
            Assert.That(measured10, Is.LessThanOrEqualTo(ten.Resources.PeakLiveCount),
                "the measured peak stays within the plan's declared bound");
            Assert.That(ten.Resources.PeakLiveCount, Is.LessThanOrEqualTo(2),
                "the linear double-buffer bound holds regardless of length");
        });
    }

    private static (long MeasuredPeak, CompiledPlan Plan) ExecuteAndMeasurePeak(EffectGraphBuilder builder)
    {
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
        using var pool = new RenderTargetPool();
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null),
            hitTest: _ => false);

        RenderNodeOperation[] outputs = PlanExecutor.Execute(
            plan, frame, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool, renderIntent: RenderIntent.Delivery);
        RenderNodeOperation.DisposeAll(outputs);
        return (pool.PeakLiveLeaseCount, plan);
    }

    [Test]
    public void FusedAndSkiaFilterPlans_RunEntirelyOnSkia()
    {
        // A fused color run and a Skia-filter run: every pass is Skia-backed, so a Vulkan-less (raster / SwiftShader)
        // context executes the whole plan (FR-014).
        CompiledPlan fused = Compile(NewBuilder().Saturate(1.3f).Brightness(1.1f).HueRotate(30f));
        CompiledPlan skia = Compile(NewBuilder().Blur(new Size(4, 4)).Dilate(2, 2));

        Assert.Multiple(() =>
        {
            Assert.That(fused.Passes.Select(p => p.Backend), Is.All.EqualTo(PassBackend.Skia));
            Assert.That(skia.Passes.Select(p => p.Backend), Is.All.EqualTo(PassBackend.Skia));
        });
    }

    [Test]
    public void ComputeNode_IsTheOnlyVulkanPass_AndAlwaysDeclaresAFallback()
    {
        CompiledPlan plan = Compile(NewBuilder()
            .Saturate(1.2f)
            .Compute(ComputeNodeDescriptor.Create(
                _ => { }, passCount: 1, BoundsContract.FullFrame, ComputeFallbackPolicy.Identity, structuralToken: "gate")));

        var compute = plan.Passes.OfType<ComputePass>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(compute.Backend, Is.EqualTo(PassBackend.Vulkan), "compute is the only Vulkan-backed pass");
            Assert.That(compute.Fallback, Is.SameAs(ComputeFallbackPolicy.Identity),
                "FR-014: the ComputeNode carries a declared no-Vulkan fallback the executor applies without a context");
        });
    }

    [Test]
    public void ComputeNode_CpuFallbackPolicy_RequiresACallback()
    {
        Assert.That(
            () => ComputeFallbackPolicy.Cpu(null!),
            Throws.ArgumentNullException);

        Assert.That(
            () => ComputeNodeDescriptor.Create(
                _ => { }, passCount: 1, BoundsContract.FullFrame, ComputeFallbackPolicy.Cpu(_ => { })),
            Throws.Nothing);
    }

    [Test]
    public void ComputeNode_InvalidDispatchFailureBehavior_IsRejected()
    {
        Assert.That(
            () => ComputeNodeDescriptor.Create(
                static _ => { }, 1, BoundsContract.Identity, ComputeFallbackPolicy.Identity,
                dispatchFailureBehavior: (ComputeDispatchFailureBehavior)int.MaxValue),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ComputeNode_LocalBounds_RemainsCoordinateInvariant()
    {
        ComputeNodeDescriptor descriptor = ComputeNodeDescriptor.Create(
            static _ => { }, 1, BoundsContract.Identity, ComputeFallbackPolicy.Identity);

        Assert.Multiple(() =>
        {
            Assert.That(descriptor.IsCoordinateInvariant, Is.True);
            Assert.That(descriptor.Bounds.RequiresFullInput, Is.False);
        });
    }

    [Test]
    public void DescriptorDefaultStructuralIdentity_AcceptsExpressionCompiledDelegates()
    {
        ParameterExpression boundsParameter = Expression.Parameter(typeof(Rect), "bounds");
        Func<Rect, Rect> boundsMap = Expression.Lambda<Func<Rect, Rect>>(
            boundsParameter, boundsParameter).Compile();
        Func<SKColorFilter?> colorFactory = Expression.Lambda<Func<SKColorFilter?>>(
            Expression.Default(typeof(SKColorFilter))).Compile();
        ParameterExpression innerParameter = Expression.Parameter(typeof(SKImageFilter), "inner");
        Func<SKImageFilter?, SKImageFilter?> skiaFactory =
            Expression.Lambda<Func<SKImageFilter?, SKImageFilter?>>(
                Expression.Default(typeof(SKImageFilter)), innerParameter).Compile();
        ParameterExpression geometryParameter = Expression.Parameter(typeof(GeometrySession), "session");
        Action<GeometrySession> geometry = Expression.Lambda<Action<GeometrySession>>(
            Expression.Empty(), geometryParameter).Compile();
        ParameterExpression computeParameter = Expression.Parameter(typeof(IComputeContext), "context");
        Action<IComputeContext> compute = Expression.Lambda<Action<IComputeContext>>(
            Expression.Empty(), computeParameter).Compile();
        ParameterExpression splitParameter = Expression.Parameter(typeof(ISplitEmitter), "emitter");
        Action<ISplitEmitter> split = Expression.Lambda<Action<ISplitEmitter>>(
            Expression.Empty(), splitParameter).Compile();
        ParameterExpression builderParameter = Expression.Parameter(typeof(EffectGraphBuilder), "builder");
        ParameterExpression branchParameter = Expression.Parameter(typeof(int), "branch");
        Action<EffectGraphBuilder, int> nested = Expression.Lambda<Action<EffectGraphBuilder, int>>(
            Expression.Empty(), builderParameter, branchParameter).Compile();

        BoundsContract bounds = default;
        ColorFilterNodeDescriptor? color = null;
        SkiaFilterNodeDescriptor? skia = null;
        GeometryNodeDescriptor? geometryNode = null;
        ComputeNodeDescriptor? computeNode = null;
        SplitNodeDescriptor? staticSplit = null;
        SplitNodeDescriptor? dynamicSplit = null;
        NestedGraphNodeDescriptor? nestedNode = null;

        Assert.DoesNotThrow(() =>
        {
            bounds = BoundsContract.Create(boundsMap, boundsMap);
            color = ColorFilterNodeDescriptor.Create(colorFactory);
            skia = SkiaFilterNodeDescriptor.Create(skiaFactory, bounds);
            geometryNode = GeometryNodeDescriptor.Create(geometry, bounds);
            computeNode = ComputeNodeDescriptor.Create(
                compute, 1, bounds, ComputeFallbackPolicy.Identity);
            staticSplit = SplitNodeDescriptor.Static(split, 1);
            dynamicSplit = SplitNodeDescriptor.Dynamic(split);
            nestedNode = NestedGraphNodeDescriptor.Create(nested);
        });

        Assert.Multiple(() =>
        {
            Assert.That(bounds.StructuralIdentity.TransformMethod, Is.SameAs(boundsMap.Method));
            Assert.That(bounds.StructuralIdentity.RequiredInputMethod, Is.SameAs(boundsMap.Method));
            Assert.That(color!.StructuralToken, Is.SameAs(colorFactory.Method));
            Assert.That(skia!.StructuralToken, Is.SameAs(skiaFactory.Method));
            Assert.That(geometryNode!.StructuralToken, Is.SameAs(geometry.Method));
            Assert.That(computeNode!.StructuralToken, Is.SameAs(compute.Method));
            Assert.That(staticSplit!.StructuralToken, Is.SameAs(split.Method));
            Assert.That(dynamicSplit!.StructuralToken, Is.SameAs(split.Method));
            Assert.That(nestedNode!.StructuralToken, Is.SameAs(nested.Method));
        });
    }
}
