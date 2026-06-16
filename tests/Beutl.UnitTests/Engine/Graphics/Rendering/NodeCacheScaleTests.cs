using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// Node cache scale tests: cache rasterizes at the renderer's density, replays tiles as At(density).
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

    // A leaf node emitting a concrete At(density) supply.
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

    // A subtree whose supply density exceeds outputScale must not be cached (would collapse working scale).
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
                    "a subtree whose supply density exceeds outputScale must not be cached");
            }
            finally
            {
                RenderNodeCacheHelper.ClearCache(node);
                node.Dispose();
            }
        });
    }

    // Caching must be behaviour-transparent: working scale must match cached or uncached.
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
                    "enabling the cache changed the resolved working scale");
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
                        // 100x100 logical at density 0.5 = 50x50 px tile.
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

    // Cache rejection is memoized; clears when the node changes. CPU-only.
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

    // Invalidate also clears the rejection.
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

    // MakeCache must mark the high-density subtree rejected so subsequent frames skip it.
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
