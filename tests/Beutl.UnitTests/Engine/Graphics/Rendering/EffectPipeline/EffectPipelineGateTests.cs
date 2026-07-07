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
        => new(s_bounds, outputScale: 1f, workingScale: 1f);

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
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool);
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
                _ => { }, passCount: 1, ComputeFallback.Identity, structuralToken: "gate")));

        var compute = plan.Passes.OfType<ComputePass>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(compute.Backend, Is.EqualTo(PassBackend.Vulkan), "compute is the only Vulkan-backed pass");
            Assert.That(compute.Fallback, Is.EqualTo(ComputeFallback.Identity),
                "FR-014: the ComputeNode carries a declared no-Vulkan fallback the executor applies without a context");
        });
    }

    [Test]
    public void ComputeNode_CpuCallbackFallback_RequiresACallback()
    {
        // A7: declaring CpuCallback without a callback is an authoring error surfaced at describe time.
        Assert.That(
            () => ComputeNodeDescriptor.Create(_ => { }, passCount: 1, ComputeFallback.CpuCallback),
            Throws.ArgumentNullException);

        Assert.That(
            () => ComputeNodeDescriptor.Create(
                _ => { }, passCount: 1, ComputeFallback.CpuCallback, cpuCallback: _ => { }),
            Throws.Nothing);
    }
}
