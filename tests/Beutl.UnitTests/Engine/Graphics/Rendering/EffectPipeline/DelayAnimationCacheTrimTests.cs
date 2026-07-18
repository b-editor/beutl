using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression coverage for the <see cref="DelayAnimationEffect"/> stable-branch resource cache (feature 004):
/// sparse branch ordinals must not allocate the ordinal's entire dense prefix, and a completed nested pass must
/// release resources for branches that are no longer live before playback can pause on the shrunken frame.
/// </summary>
[NonParallelizable]
[TestFixture]
public class DelayAnimationCacheTrimTests
{
    private static readonly Rect s_bounds = new(0, 0, 64, 64);

    [Test]
    public void PausingAfterBranchCountShrinks_ReleasesStaleResourcesInTheSamePass()
    {
        var effect = new DelayAnimationEffect();
        effect.Effect.CurrentValue = new Blur { Sigma = { CurrentValue = new Size(3, 3) } };
        using var resource = (DelayAnimationEffect.Resource)effect.ToResource(CompositionContext.Default);

        // Pass 1: five branches grow the cache to five entries.
        ExecutePass(effect, resource, branchCount: 5);
        Assert.That(resource.DelayedResources, Has.Count.EqualTo(5), "five branches grow the cache to five entries");
        FilterEffect.Resource[] stale =
        [
            resource.DelayedResources[2],
            resource.DelayedResources[3],
            resource.DelayedResources[4],
        ];

        // Pass 2: two branches, followed by a paused timeline (there is deliberately no third describe).
        ExecutePass(effect, resource, branchCount: 2);

        Assert.Multiple(() =>
        {
            Assert.That(resource.DelayedResources, Has.Count.EqualTo(2),
                "the pass that observes the shrink must release stale resources before playback can pause");
            foreach (FilterEffect.Resource staleResource in stale)
                Assert.That(staleResource.IsDisposed, Is.True, "each resource removed from the live set is disposed");
        });
    }

    [Test]
    public void SparseHighBranchOrdinal_AllocatesOnlyTheLiveResource()
    {
        var effect = new DelayAnimationEffect();
        effect.Effect.CurrentValue = new Blur { Sigma = { CurrentValue = new Size(3, 3) } };
        using var resource = (DelayAnimationEffect.Resource)effect.ToResource(CompositionContext.Default);

        DescribeBranch(effect, resource, branchOrdinal: 4095);
        FilterEffect.Resource delayed = resource.DelayedResources[4095];
        DescribeBranch(effect, resource, branchOrdinal: 4095);

        Assert.Multiple(() =>
        {
            Assert.That(resource.DelayedResources, Has.Count.EqualTo(1),
                "stable branch ordinal 4095 must not allocate 4095 unused prefix resources");
            Assert.That(resource.DelayedResources.Keys, Is.EquivalentTo(new[] { 4095 }));
            Assert.That(resource.DelayedResources[4095], Is.SameAs(delayed),
                "the same stable ordinal reuses its existing child resource");
        });
    }

    private static void ExecutePass(
        DelayAnimationEffect effect,
        DelayAnimationEffect.Resource resource,
        int branchCount)
    {
        var builder = new EffectGraphBuilder(
            s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        builder.Split(SplitNodeDescriptor.Static(
            emitter =>
            {
                for (int i = 0; i < branchCount; i++)
                {
                    emitter.Emit(emitter.Input.Bounds, session =>
                    {
                        ImmediateCanvas canvas = session.OpenCanvas();
                        using (canvas.PushDeviceSpace())
                            session.Inputs[0].Draw(canvas, default);
                    });
                }
            },
            branchCount,
            structuralToken: $"delay-cache-split-{branchCount}"));
        effect.Describe(builder, resource);
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null),
            hitTest: s_bounds.Contains);
        RenderNodeOperation[] outputs = PlanExecutor.Execute(
            plan, frame, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: null,
            renderIntent: RenderIntent.Delivery);
        RenderNodeOperation.DisposeAll(outputs);
    }

    private static void DescribeBranch(
        DelayAnimationEffect effect,
        DelayAnimationEffect.Resource resource,
        int branchOrdinal)
    {
        var builder = new EffectGraphBuilder(
            s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        effect.Describe(builder, resource);
        using EffectGraph graph = builder.Build();
        var descriptor = (NestedGraphNodeDescriptor)graph.Nodes[0].Descriptor;
        var branchBuilder = new EffectGraphBuilder(
            s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        descriptor.DescribeBranch(branchBuilder, branchOrdinal);
        using EffectGraph branchGraph = branchBuilder.Build();
    }
}
