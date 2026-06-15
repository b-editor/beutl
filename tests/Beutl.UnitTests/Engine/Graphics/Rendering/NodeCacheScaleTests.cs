using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// feature 003 (FR-020 minimal cache fix): the node cache participates in the scale model —
// it rasterizes at the renderer's density under the FR-037 ceiling, records it, and replays
// tiles tagged At(density) so a cached subtree reports a concrete supply instead of flipping
// to Unbounded (which would shift downstream working scales when the cache kicks in).
// Cross-scale cache REUSE stays deferred (T025); these tests pin the within-one-renderer contract.
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

    // A leaf node that emits a CONCRETE supply density At(density) — a stand-in for a transform-densified
    // high-resolution bitmap source (e.g. a 4K image shrunk onto a 1080 timeline reports At(4)).
    private sealed class ConcreteSourceNode(float density) : RenderNode
    {
        public override RenderNodeOperation[] Process(RenderNodeContext context)
            => [RenderNodeOperation.CreateLambda(
                new Rect(0, 0, 100, 100),
                canvas => canvas.DrawRectangle(new Rect(0, 0, 100, 100), Brushes.Resource.White, null),
                hitTest: _ => false,
                effectiveScale: EffectiveScale.At(density))];
    }

    private static float PullSingleDensity(RenderNode node, bool useRenderCache, float outputScale)
    {
        var processor = new RenderNodeProcessor(node, useRenderCache, outputScale, maxWorkingScale: 8f);
        RenderNodeOperation[] ops = processor.PullToRoot();
        try
        {
            Assert.That(ops, Is.Not.Empty);
            return ops[0].EffectiveScale.Value;
        }
        finally
        {
            foreach (RenderNodeOperation op in ops) op.Dispose();
        }
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

    // feature 003 (FR-018, I4 cache-density-collapse fix): a subtree whose supply density exceeds outputScale
    // must NOT be cached — the cache rasterizes at outputScale, discarding the extra detail and silently
    // lowering every downstream effect's working scale once the cache kicks in.
    [Test]
    public void CreateDefaultCache_RefusesToCache_WhenSupplyDensityExceedsOutputScale()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var node = new ConcreteSourceNode(4f);
            node.Cache.ReportRenderCount(RenderNodeCache.Count);
            try
            {
                RenderNodeCacheHelper.CreateDefaultCache(
                    node, RenderCacheOptions.Default, outputScale: 1f, maxWorkingScale: 8f);

                Assert.That(node.Cache.IsCached, Is.False,
                    "a subtree whose supply density (4) exceeds outputScale (1) must not be cached — caching "
                    + "would collapse the working scale of a downstream effect (I4 / FR-018)");
            }
            finally
            {
                RenderNodeCacheHelper.ClearCache(node);
                node.Dispose();
            }
        });
    }

    // Caching must be behaviour-transparent: the working scale a downstream boundary resolves must be the same
    // cached or uncached. With the I4 fix the high-density subtree is not cached, so the replayed
    // (= freshly re-processed) density still matches the uncached supply.
    [Test]
    public void HighDensitySubtree_CacheReplay_MatchesUncachedWorkingScale()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var node = new ConcreteSourceNode(4f);
            node.Cache.ReportRenderCount(RenderNodeCache.Count);
            try
            {
                float uncached = PullSingleDensity(node, useRenderCache: false, outputScale: 1f);
                RenderNodeCacheHelper.MakeCache(node, RenderCacheOptions.Default, outputScale: 1f, maxWorkingScale: 8f);
                float cached = PullSingleDensity(node, useRenderCache: true, outputScale: 1f);

                Assert.That(uncached, Is.EqualTo(4f), "the uncached supply density must be the source's At(4)");
                Assert.That(cached, Is.EqualTo(uncached),
                    "enabling the cache changed the resolved working scale — caching is not behaviour-transparent (I4)");
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

    // feature 003 follow-up (per-frame cache re-rejection): once CreateDefaultCache refuses a subtree, the
    // rejection must be remembered so MakeCache stops re-pulling + re-rejecting it every frame. The memo clears
    // when the node changes so a fresh attempt can happen after the subtree re-warms. CPU-only.
    [Test]
    public void RejectedCache_IsMemoized_AndClearsWhenNodeChanges()
    {
        var node = new ConcreteSourceNode(4f);
        try
        {
            RenderNodeCache cache = node.Cache;
            cache.ReportRenderCount(RenderNodeCache.Count);
            Assert.That(cache.CanCache(), Is.True);
            Assert.That(cache.IsCacheRejected, Is.False);

            cache.RejectCache();
            Assert.That(cache.IsCacheRejected, Is.True,
                "a refused subtree must stay marked so MakeCache does not re-attempt it every frame");

            node.HasChanges = true;
            cache.IncrementRenderCount();
            Assert.That(cache.IsCacheRejected, Is.False, "a node change must clear the rejection");
            Assert.That(cache.CanCache(), Is.False, "a node change must reset the render count");
        }
        finally
        {
            node.Dispose();
        }
    }

    // Invalidate (cache eviction / replacement) also clears the rejection so a re-warmed subtree can be retried.
    [Test]
    public void RejectedCache_ClearsOnInvalidate()
    {
        var node = new ConcreteSourceNode(4f);
        try
        {
            node.Cache.RejectCache();
            Assert.That(node.Cache.IsCacheRejected, Is.True);

            node.Cache.Invalidate();
            Assert.That(node.Cache.IsCacheRejected, Is.False);
        }
        finally
        {
            node.Dispose();
        }
    }

    // End-to-end: MakeCache must MARK the high-density subtree rejected (not merely decline to cache it), so the
    // next frame skips the redundant CreateDefaultCache pull instead of re-rejecting.
    [Test]
    public void MakeCache_HighDensitySubtree_MarksCacheRejected()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var node = new ConcreteSourceNode(4f);
            node.Cache.ReportRenderCount(RenderNodeCache.Count);
            try
            {
                RenderNodeCacheHelper.MakeCache(node, RenderCacheOptions.Default, outputScale: 1f, maxWorkingScale: 8f);

                Assert.That(node.Cache.IsCached, Is.False);
                Assert.That(node.Cache.IsCacheRejected, Is.True,
                    "the high-density rejection must be memoized so MakeCache stops re-pulling the subtree every frame");
            }
            finally
            {
                RenderNodeCacheHelper.ClearCache(node);
                node.Dispose();
            }
        });
    }
}
