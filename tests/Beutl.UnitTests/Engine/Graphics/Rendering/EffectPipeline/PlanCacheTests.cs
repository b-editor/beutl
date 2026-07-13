using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Pins the C5 invalidation edges of <see cref="PlanCache"/>: a hit requires <b>both</b> an equal
/// <see cref="StructuralKey"/> and the same graphics-context identity <em>by reference</em> — a context recreated
/// after device loss is a new reference, so it can never stale-hit a plan compiled for the lost device.
/// </summary>
[TestFixture]
public class PlanCacheTests
{
    private static readonly Rect s_bounds = new(0, 0, 64, 48);

    private static (StructuralKey Key, CompiledPlan Plan) Compile(float saturation = 1.2f)
    {
        EffectGraphBuilder builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f)
            .Saturate(saturation);
        using EffectGraph graph = builder.Build();
        return (StructuralKey.Compute(graph), EffectGraphCompiler.Compile(graph, diagnostics: null));
    }

    private static (StructuralKey Key, CompiledPlan Plan) CompileCompute(bool usesDepthScratch)
    {
        EffectGraphBuilder builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f)
            .Compute(ComputeNodeDescriptor.Create(
                static _ => { }, passCount: 1, ComputeFallback.Identity,
                depthScratchCount: usesDepthScratch ? 1 : 0,
                structuralToken: "depth-cache"));
        using EffectGraph graph = builder.Build();
        return (StructuralKey.Compute(graph), EffectGraphCompiler.Compile(graph, diagnostics: null));
    }

    // B3: a compute descriptor changing DepthScratchCount under the same structural token must miss the plan cache — a
    // stale hit would reuse a resource plan that under-declares the depth intermediate (C3/C5, FR-007 peak-live).
    [Test]
    public void TryGet_ComputeDepthRequirementToggled_Misses()
    {
        var cache = new PlanCache();
        var context = new object();
        (StructuralKey depthKey, CompiledPlan depthPlan) = CompileCompute(usesDepthScratch: true);
        cache.Store(depthKey, context, depthPlan);

        (StructuralKey noDepthKey, _) = CompileCompute(usesDepthScratch: false);
        Assert.That(cache.TryGet(noDepthKey, context, out _), Is.False,
            "toggling the structural depth requirement must miss the plan cache (recompile exactly once)");

        (StructuralKey sameKey, _) = CompileCompute(usesDepthScratch: true);
        Assert.That(cache.TryGet(sameKey, context, out CompiledPlan hit), Is.True,
            "an unchanged depth requirement re-describes to an equal key and hits");
        Assert.That(hit, Is.SameAs(depthPlan));
    }

    [Test]
    public void TryGet_EqualKeySameContext_Hits()
    {
        var cache = new PlanCache();
        var context = new object();
        (StructuralKey key, CompiledPlan plan) = Compile();
        cache.Store(key, context, plan);

        // A parameter-only change (a different saturation amount) produces an equal structural key.
        (StructuralKey reDescribedKey, _) = Compile(saturation: 3.5f);

        Assert.That(cache.TryGet(reDescribedKey, context, out CompiledPlan hit), Is.True);
        Assert.That(hit, Is.SameAs(plan));
    }

    [Test]
    public void TryGet_RecreatedContext_MissesEvenWithEqualKey()
    {
        var cache = new PlanCache();
        (StructuralKey key, CompiledPlan plan) = Compile();
        cache.Store(key, new object(), plan);

        Assert.That(cache.TryGet(key, new object(), out _), Is.False,
            "a device-lost/recreated context is a new reference and must miss (C5)");
    }

    [Test]
    public void Store_UnderRecreatedContext_ReplacesTheLostDeviceEntry()
    {
        var cache = new PlanCache();
        var lost = new object();
        var recreated = new object();
        (StructuralKey key, CompiledPlan plan) = Compile();
        cache.Store(key, lost, plan);

        Assert.That(cache.TryGet(key, recreated, out _), Is.False, "the recreated device misses first");

        (_, CompiledPlan recompiled) = Compile();
        cache.Store(key, recreated, recompiled);

        Assert.That(cache.TryGet(key, recreated, out CompiledPlan hit), Is.True);
        Assert.That(hit, Is.SameAs(recompiled));
        Assert.That(cache.TryGet(key, lost, out _), Is.False,
            "the lost device's entry is gone (single-entry cache)");
    }

    [Test]
    public void Invalidate_DropsTheEntry()
    {
        var cache = new PlanCache();
        var context = new object();
        (StructuralKey key, CompiledPlan plan) = Compile();
        cache.Store(key, context, plan);

        cache.Invalidate();

        Assert.That(cache.TryGet(key, context, out _), Is.False);
    }
}
