using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

public class RenderNodeCacheHelperTest
{
    [Test]
    public void DefaultPolicy_IsDisabledAndCacheRequiresExplicitOptIn()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RenderCacheOptions.Default.IsEnabled, Is.False);
            Assert.That(RenderCacheOptions.Default, Is.SameAs(RenderCacheOptions.Disabled));
            Assert.That(RenderCacheOptions.Enabled.IsEnabled, Is.True);
        });
    }

    [Test]
    public void CanCacheRecursive_ShouldReturnFalse_WhenCacheCannotCache()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);

        // Act
        bool result = RenderNodeCacheHelper.CanCacheRecursive(containerNode);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CanCacheRecursive_ShouldReturnTrue_WhenCacheCanCache()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);
        childNode.Cache.ReportRenderCount(3);
        containerNode.Cache.ReportRenderCount(3);

        // Act
        bool result = RenderNodeCacheHelper.CanCacheRecursive(containerNode);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void CanCacheRecursive_ShouldReturnFalse_WhenChildCountIsDifferent()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);
        childNode.Cache.ReportRenderCount(3);
        containerNode.Cache.ReportRenderCount(3);
        containerNode.AddChild(new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null));

        // Act
        bool result = RenderNodeCacheHelper.CanCacheRecursive(containerNode);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CanCacheRecursive_ShouldReturnFalse_WhenChildIsDifferent()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);
        childNode.Cache.ReportRenderCount(3);
        containerNode.Cache.ReportRenderCount(3);
        containerNode.SetChild(0, new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null));

        // Act
        bool result = RenderNodeCacheHelper.CanCacheRecursive(containerNode);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CanCacheRecursive_ShouldFollowEveryCompleteTargetRoot()
    {
        using var first = new ContainerRenderNode();
        using var second = new ContainerRenderNode();
        using var completeTarget = new CompleteTargetRenderNode(first, [second]);
        completeTarget.Cache.ReportRenderCount(RenderNodeCache.Count);
        first.Cache.ReportRenderCount(RenderNodeCache.Count);

        bool withColdRoot = RenderNodeCacheHelper.CanCacheRecursive(completeTarget);
        second.Cache.ReportRenderCount(RenderNodeCache.Count);

        Assert.Multiple(() =>
        {
            Assert.That(withColdRoot, Is.False, "a root the complete target records through went unvisited");
            Assert.That(RenderNodeCacheHelper.CanCacheRecursive(completeTarget), Is.True);
        });
    }

    [Test]
    public void ClearCache_ShouldInvalidateCache()
    {
        // Arrange
        using var node = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        Rect bounds = new(0, 0, 100, 100);
        using (var renderTarget = RenderTarget.CreateNull(100, 100))
        {
            RenderNodeCache.PublishAtomically(
                [RenderCacheTestSupport.CreatePublication(node.Cache, renderTarget, bounds)]);
        }

        // Act
        RenderNodeCacheHelper.ClearCache(node);

        // Assert
        Assert.That(node.Cache.IsCached, Is.False);
    }

    [Test]
    public void ClearCache_ShouldInvalidateCache_WhenNodeIsContainerRenderNode()
    {
        // Arrange
        using var node = new ContainerRenderNode();
        using var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        Rect bounds = new(0, 0, 100, 100);
        using (var renderTarget = RenderTarget.CreateNull(100, 100))
        {
            RenderNodeCache.PublishAtomically(
                [RenderCacheTestSupport.CreatePublication(childNode.Cache, renderTarget, bounds)]);
        }
        node.AddChild(childNode);

        // Act
        RenderNodeCacheHelper.ClearCache(node);

        // Assert
        Assert.That(childNode.Cache.IsCached, Is.False);
    }

    [Test]
    public void FrameRequest_ShouldPublishEligibleCacheCandidates()
    {
        using var containerNode = new ContainerRenderNode();
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        containerNode.AddChild(childNode);
        childNode.Cache.ReportRenderCount(3);
        containerNode.Cache.ReportRenderCount(3);
        using var renderer = CreateFrameRenderer(containerNode);

        using (renderer.Rasterize())
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(containerNode.Cache.IsCached, Is.True);
            Assert.That(childNode.Cache.IsCached, Is.True);
        });
    }

    [Test]
    public void FrameRequest_ShouldNotPublishWhenCachePolicyIsDisabled()
    {
        using var containerNode = new ContainerRenderNode();
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        containerNode.AddChild(childNode);
        childNode.Cache.ReportRenderCount(3);
        containerNode.Cache.ReportRenderCount(3);
        using var renderer = CreateFrameRenderer(containerNode, useRenderCache: false);

        using (renderer.Rasterize())
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(containerNode.Cache.IsCached, Is.False);
            Assert.That(childNode.Cache.IsCached, Is.False);
        });
    }

    [Test]
    public void FrameRequest_ShouldSelectWarmParentWithoutRequiringWarmChildren()
    {
        using var containerNode = new ContainerRenderNode();
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        containerNode.AddChild(childNode);
        containerNode.Cache.ReportRenderCount(3);
        using var renderer = CreateFrameRenderer(containerNode);

        using (renderer.Rasterize())
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(containerNode.Cache.IsCached, Is.True);
            Assert.That(childNode.Cache.IsCached, Is.False);
        });
    }

    [TestCase(10_404, true)]
    [TestCase(10_403, false)]
    public void FrameRequest_ShouldApplyConfiguredCacheRulesToPhysicalCapture(int maxPixels, bool expectedCached)
    {
        using var containerNode = new ContainerRenderNode();
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        containerNode.AddChild(childNode);
        childNode.Cache.ReportRenderCount(3);
        containerNode.Cache.ReportRenderCount(3);
        using var renderer = CreateFrameRenderer(
            containerNode,
            cacheRules: new RenderCacheRules(maxPixels, 1));

        using (renderer.Rasterize())
        {
        }

        Assert.That(containerNode.Cache.IsCached, Is.EqualTo(expectedCached));
    }

    private static RenderNodeRenderer CreateFrameRenderer(
        RenderNode node,
        bool useRenderCache = true,
        RenderCacheRules? cacheRules = null)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = new Rect(0, 0, 100, 100),
                    CacheOptions = new RenderCacheOptions(
                        useRenderCache,
                        cacheRules ?? RenderCacheRules.Default),
                    Purpose = RenderRequestPurpose.Frame,
                },
                TargetFactory = new CpuTargetFactory(),
            });

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        private static readonly SKColorSpace s_colorSpace = SKColorSpace.CreateSrgbLinear();

        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize);

        private sealed class CpuRenderTarget : RenderTarget
        {
            public CpuRenderTarget(PixelSize size)
                : base(
                    SKSurface.Create(new SKImageInfo(
                        size.Width,
                        size.Height,
                        SKColorType.RgbaF16,
                        SKAlphaType.Premul,
                        s_colorSpace))
                    ?? throw new InvalidOperationException("Could not create a CPU cache-test target."),
                    size.Width,
                    size.Height)
            {
            }
        }
    }
}
