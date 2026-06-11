using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// feature 003 (FR-020 minimal cache fix): the node cache must participate in the scale model —
// it rasterizes at the renderer's density under the FR-037 ceiling, records that density, and
// replays its tiles tagged At(density) so a cached subtree keeps reporting a concrete supply
// instead of flipping to Unbounded (which would change downstream working scales whenever the
// cache kicks in). Cross-scale cache REUSE stays deferred (T025); these tests pin the
// within-one-renderer contract.
[NonParallelizable]
[TestFixture]
public class NodeCacheScaleTests
{
    private static EllipseRenderNode CacheableEllipse()
    {
        var node = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        return node;
    }

    [TestCase(0.5f)]
    [TestCase(1.0f)]
    public void CreateDefaultCache_RecordsCreationDensity_AndReplayReportsIt(float outputScale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            EllipseRenderNode node = CacheableEllipse();
            try
            {
                RenderNodeCacheHelper.MakeCache(
                    node, RenderCacheOptions.Default, outputScale, maxWorkingScale: 2f * outputScale);

                Assert.That(node.Cache.IsCached, Is.True);
                Assert.That(node.Cache.Density, Is.EqualTo(outputScale));

                var processor = new RenderNodeProcessor(
                    node, useRenderCache: true, outputScale, maxWorkingScale: 2f * outputScale);
                RenderNodeOperation[] ops = processor.PullToRoot();
                try
                {
                    Assert.That(ops, Is.Not.Empty);
                    foreach (RenderNodeOperation op in ops)
                    {
                        Assert.That(op.EffectiveScale.IsUnbounded, Is.False,
                            "a cached tile is a concrete bitmap and must not replay as Unbounded");
                        Assert.That(op.EffectiveScale.Value, Is.EqualTo(outputScale));
                    }
                }
                finally
                {
                    foreach (RenderNodeOperation op in ops) op.Dispose();
                }
            }
            finally
            {
                RenderNodeCacheHelper.ClearCache(node);
                node.Dispose();
            }
        });
    }

    [Test]
    public void CreateDefaultCache_TileSize_MatchesCreationDensity()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            EllipseRenderNode node = CacheableEllipse();
            try
            {
                RenderNodeCacheHelper.CreateDefaultCache(
                    node, RenderCacheOptions.Default, outputScale: 0.5f, maxWorkingScale: 1f);

                Assert.That(node.Cache.IsCached, Is.True);
                foreach ((RenderTarget rt, Rect bounds) in node.Cache.UseCache())
                {
                    using (rt)
                    {
                        // A 100x100 logical ellipse cached at density 0.5 is a 50x50 px tile,
                        // not the density-1 100x100 the old default-scale processor produced.
                        Assert.That(rt.Width, Is.EqualTo((int)Math.Ceiling(bounds.Width * 0.5f)));
                        Assert.That(rt.Height, Is.EqualTo((int)Math.Ceiling(bounds.Height * 0.5f)));
                    }
                }
            }
            finally
            {
                RenderNodeCacheHelper.ClearCache(node);
                node.Dispose();
            }
        });
    }
}
