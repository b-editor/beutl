using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// The cache warm-up render (<see cref="RenderNodeCacheHelper.CreateDefaultCache"/>) runs with the renderer's
/// diagnostics and pool: its effect INTERMEDIATES pool and count like any other frame, while the retained cache
/// targets themselves stay non-pooled (<c>RasterizeAt</c> allocates directly), so no pooled lease outlives the
/// warm-up. Previously the warm-up processor was constructed bare, so a cacheable effect subtree allocated every
/// intermediate outside the pool and those passes were invisible to <c>IRenderer.Diagnostics</c>.
/// </summary>
[NonParallelizable]
[TestFixture]
public class CacheWarmupPoolingTests
{
    [Test]
    public void CreateDefaultCache_EffectSubtree_PoolsIntermediatesAndLeavesNoLiveLease()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var gamma = new Gamma();
            gamma.Amount.CurrentValue = 150f;
            var resource = (FilterEffect.Resource)gamma.ToResource(CompositionContext.Default);

            var child = new EllipseRenderNode(new Rect(0, 0, 64, 64), Brushes.Resource.White, null);
            using var effectNode = new PlanFilterEffectRenderNode(resource);
            effectNode.AddChild(child);
            var container = new ContainerRenderNode();
            container.AddChild(effectNode);
            foreach (RenderNode node in (RenderNode[])[child, effectNode, container])
                node.Cache.ReportRenderCount(3);

            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();

            RenderNodeCacheHelper.MakeCache(
                container, new RenderCacheOptions(true, RenderCacheRules.Default),
                RenderIntent.Delivery, outputScale: 1f,
                maxWorkingScale: float.PositiveInfinity, diagnostics, pool);

            Assert.Multiple(() =>
            {
                Assert.That(container.Cache.IsCached, Is.True, "sanity: the subtree was cached");
                Assert.That(diagnostics.PoolAcquires, Is.GreaterThan(0),
                    "the warm-up render's effect intermediates acquire from the renderer's pool and are counted");
                Assert.That(pool.LiveLeaseCount, Is.Zero,
                    "the retained cache targets are non-pooled, so no pooled lease outlives the warm-up");
            });
        });
    }
}
