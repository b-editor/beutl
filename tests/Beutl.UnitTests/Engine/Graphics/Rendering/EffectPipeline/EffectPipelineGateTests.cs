using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// The remaining structural gates for feature 004 step 5b (T050): FR-007 peak-intermediates (a longer chain never
/// raises the peak-live intermediate count above a shorter same-shape chain — the double-buffer bound, measured as
/// the <see cref="ResourcePlan"/> lifetime-interval overlap) and FR-014 no-Vulkan safety (fused / Skia-filter plans
/// run entirely on the Skia backend, so a raster/Skia-only context executes them; a ComputeNode is the only
/// Vulkan-backed pass and always declares a fallback). All assertions are compiler/descriptor-level, so they need
/// no GPU.
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

    [Test]
    public void TenEffectChain_PeakLiveNotAboveThreeEffectChain()
    {
        CompiledPlan three = Compile(AlternatingChain(3));
        CompiledPlan ten = Compile(AlternatingChain(10));

        Assert.Multiple(() =>
        {
            Assert.That(ten.Passes.Length, Is.GreaterThan(three.Passes.Length),
                "the 10-effect chain really is a longer schedule");
            Assert.That(ten.Resources.PeakLiveCount, Is.LessThanOrEqualTo(three.Resources.PeakLiveCount),
                "FR-007: a longer chain never needs more peak-live intermediates than a shorter same-shape one");
            Assert.That(ten.Resources.PeakLiveCount, Is.LessThanOrEqualTo(2),
                "the linear double-buffer bound holds regardless of length");
        });
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
