using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Regression for the <see cref="DelayAnimationEffect"/> per-branch resource cache that only ever grew (feature 004):
/// its delayed-resource list is extended in the nested-graph branch callback but was never trimmed, so a describe
/// that fanned out into fewer branches than an earlier one retained the now-unused child resources until the effect
/// was disposed. The effect now trims the cache to the previously observed branch count on each describe, disposing
/// the excess entries.
/// </summary>
[TestFixture]
public class DelayAnimationCacheTrimTests
{
    private static readonly Rect s_bounds = new(0, 0, 64, 64);

    [Test]
    public void ShrinkingBranchCount_TrimsAndDisposesExcessDelayedResources()
    {
        var effect = new DelayAnimationEffect();
        effect.Effect.CurrentValue = new Blur { Sigma = { CurrentValue = new Size(3, 3) } };
        var resource = (DelayAnimationEffect.Resource)effect.ToResource(CompositionContext.Default);

        // Pass 1: five branches grow the cache to five entries.
        DescribePass(effect, resource, branchCount: 5);
        Assert.That(resource.DelayedResources, Has.Count.EqualTo(5), "five branches grow the cache to five entries");
        FilterEffect.Resource[] excess =
        [
            resource.DelayedResources[2],
            resource.DelayedResources[3],
            resource.DelayedResources[4],
        ];

        // Pass 2: two branches. The trim runs at describe time against the PREVIOUS pass's observed count (5), so the
        // stale entries are still present after this pass; this pass records the new, smaller high-water (2).
        DescribePass(effect, resource, branchCount: 2);

        // Pass 3: the describe trims the cache down to the branch count observed in pass 2, disposing entries 2..4.
        DescribePass(effect, resource, branchCount: 2);

        Assert.Multiple(() =>
        {
            Assert.That(resource.DelayedResources, Has.Count.EqualTo(2), "the cache trims to the current branch count");
            foreach (FilterEffect.Resource stale in excess)
                Assert.That(stale.IsDisposed, Is.True, "each trimmed delayed resource is disposed");
        });

        resource.Dispose();
    }

    // Runs one describe pass: describes the effect (registers the nested-graph node and trims), then drives the
    // node's per-branch callback branchCount times, exactly as the executor's ExecuteNestedGraph would.
    private static void DescribePass(DelayAnimationEffect effect, FilterEffect.Resource resource, int branchCount)
    {
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
        effect.Describe(builder, resource);
        using EffectGraph graph = builder.Build();
        var descriptor = (NestedGraphNodeDescriptor)graph.Nodes[0].Descriptor;
        for (int i = 0; i < branchCount; i++)
        {
            var branchBuilder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
            descriptor.DescribeBranch(branchBuilder, i);
        }
    }
}
