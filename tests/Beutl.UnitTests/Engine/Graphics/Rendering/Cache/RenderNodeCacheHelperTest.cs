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
    public void Lifecycle_FollowsReferencedChildNodesForInvalidation()
    {
        using var child = new ContainerRenderNode();
        using var root = new ReferencesChildRenderNode(child);
        RenderNodeCache.PublishAtomically(
        [
            RenderCacheTestSupport.CreatePublication(root.Cache, RenderTarget.CreateNull(1, 1), new Rect(0, 0, 1, 1)),
            RenderCacheTestSupport.CreatePublication(child.Cache, RenderTarget.CreateNull(1, 1), new Rect(0, 0, 1, 1)),
        ]);
        child.HasChanges = true;

        RenderNodeCacheHelper.BeginLifecycle(root);

        Assert.Multiple(() =>
        {
            Assert.That(root.Cache.IsCached, Is.False);
            Assert.That(child.Cache.IsCached, Is.False);
        });
    }

    [Test]
    public void Lifecycle_WithoutSuccessfulCompletion_DoesNotClearDirtyFlags()
    {
        using var root = new ContainerRenderNode { HasChanges = true };

        _ = RenderNodeCacheHelper.BeginLifecycle(root);

        Assert.That(root.HasChanges, Is.True);
    }

    [Test]
    public void ClearOwnedCaches_ShouldInvalidateCache()
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
        RenderNodeCacheHelper.ClearOwnedCaches(node);

        // Assert
        Assert.That(node.Cache.IsCached, Is.False);
    }

    [Test]
    public void ClearOwnedCaches_ShouldInvalidateCache_WhenNodeIsContainerRenderNode()
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
        RenderNodeCacheHelper.ClearOwnedCaches(node);

        // Assert
        Assert.That(childNode.Cache.IsCached, Is.False);
    }

    [Test]
    public void DirectFrameRequests_WarmAutomaticallyAndPublishEligibleCacheCandidates()
    {
        using var containerNode = new ContainerRenderNode();
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        containerNode.AddChild(childNode);
        containerNode.HasChanges = true;
        using var renderer = CreateFrameRenderer(containerNode);

        RenderRequests(renderer, RenderNodeCache.StableRequestCount + 2);

        Assert.Multiple(() =>
        {
            Assert.That(containerNode.HasChanges, Is.False);
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
        containerNode.HasChanges = true;
        using var renderer = CreateFrameRenderer(containerNode, useRenderCache: false);

        RenderRequests(renderer, RenderNodeCache.StableRequestCount + 2);

        Assert.Multiple(() =>
        {
            Assert.That(containerNode.Cache.IsCached, Is.False);
            Assert.That(childNode.Cache.IsCached, Is.False);
        });
    }

    [Test]
    public void DirectFrameRequest_DoesNotCaptureBeforeAutomaticWarmupCompletes()
    {
        using var containerNode = new ContainerRenderNode();
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        containerNode.AddChild(childNode);
        using var renderer = CreateFrameRenderer(containerNode);

        RenderRequests(renderer, RenderNodeCache.StableRequestCount);

        Assert.Multiple(() =>
        {
            Assert.That(containerNode.Cache.IsCached, Is.False);
            Assert.That(childNode.Cache.IsCached, Is.False);
        });

        RenderRequests(renderer, 1);

        Assert.Multiple(() =>
        {
            Assert.That(containerNode.Cache.IsCached, Is.True);
            Assert.That(childNode.Cache.IsCached, Is.True);
        });
    }

    [Test]
    public void DirectFrameRequest_WhenRecordingFails_RetainsDirtyFlag()
    {
        using var node = new ThrowingRenderNode { HasChanges = true };
        using var renderer = CreateFrameRenderer(node);

        Assert.That(
            () => renderer.Rasterize(),
            Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("recording failed"));

        Assert.Multiple(() =>
        {
            Assert.That(node.HasChanges, Is.True);
            Assert.That(node.Cache.SuccessfulStableRequestCount, Is.Zero);
        });
    }

    [Test]
    public void DirectFrameRequest_CallStateChangesReuseWarmCacheUntilHasChangesIsSet()
    {
        using var node = new StatefulCallNode(Colors.Red);
        using var renderer = CreateFrameRenderer(node);

        RenderRequests(renderer, RenderNodeCache.StableRequestCount + 1);
        int warmedExecutionCount = node.ExecutionCount;

        Assert.That(node.Cache.IsCached, Is.True);

        node.SetCallState(Colors.Blue, reportChanges: false);
        using (renderer.Rasterize())
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(node.ExecutionCount, Is.EqualTo(warmedExecutionCount),
                "Call state alone must not replace a warmed output cache entry.");
            Assert.That(node.Cache.IsCached, Is.True);
        });

        node.SetCallState(Colors.Green, reportChanges: true);
        using (renderer.Rasterize())
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(node.ExecutionCount, Is.EqualTo(warmedExecutionCount + 1),
                "HasChanges must evict the warm output and execute the newly recorded Call state.");
            Assert.That(node.HasChanges, Is.False,
                "The successful request, not the state setter, consumes the invalidation signal.");
            Assert.That(node.Cache.IsCached, Is.False);
            Assert.That(node.Cache.SuccessfulStableRequestCount, Is.Zero);
        });
    }

    [Test]
    public void DirectFrameRequests_DirtyParentAndChangingSiblingEachFrame_WarmAndReuseStableChildCache()
    {
        using var parent = new ContainerRenderNode();
        using var stable = new StatefulCallNode(Colors.Red);
        using var changing = new StatefulCallNode(Colors.Blue);
        parent.AddChild(stable);
        parent.AddChild(changing);
        using var renderer = CreateFrameRenderer(parent);

        for (int frame = 0; frame <= RenderNodeCache.StableRequestCount; frame++)
        {
            parent.HasChanges = true;
            changing.SetCallState(
                frame % 2 == 0 ? Colors.Blue : Colors.Green,
                reportChanges: true);
            using RenderNodeRasterization rasterization = renderer.Rasterize();
        }

        int stableExecutionCount = stable.ExecutionCount;
        int changingExecutionCount = changing.ExecutionCount;
        Assert.Multiple(() =>
        {
            Assert.That(parent.Cache.CanCapture, Is.False, "the dirty parent restarts warm-up each frame");
            Assert.That(parent.Cache.IsCached, Is.False);
            Assert.That(changing.Cache.CanCapture, Is.False, "the changing child restarts warm-up each frame");
            Assert.That(changing.Cache.IsCached, Is.False);
            Assert.That(stable.Cache.CanCapture, Is.True, "the unchanged child reaches cache admission");
            Assert.That(stable.Cache.IsCached, Is.True, "the unchanged child publishes a reusable output");
        });

        parent.HasChanges = true;
        changing.SetCallState(Colors.Purple, reportChanges: true);
        using (renderer.Rasterize())
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(parent.Cache.CanCapture, Is.False);
            Assert.That(changing.Cache.CanCapture, Is.False);
            Assert.That(changing.ExecutionCount, Is.EqualTo(changingExecutionCount + 1));
            Assert.That(stable.Cache.CanCapture, Is.True);
            Assert.That(stable.Cache.IsCached, Is.True);
            Assert.That(stable.ExecutionCount, Is.EqualTo(stableExecutionCount),
                "the stable child output is reused while its parent and sibling change.");
        });
    }

    [TestCase(10_404, true)]
    [TestCase(10_403, false)]
    public void FrameRequest_ShouldApplyConfiguredCacheRulesToPhysicalCapture(int maxPixels, bool expectedCached)
    {
        using var containerNode = new ContainerRenderNode();
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        containerNode.AddChild(childNode);
        containerNode.HasChanges = true;
        using var renderer = CreateFrameRenderer(
            containerNode,
            cacheRules: new RenderCacheRules(maxPixels, 1));

        RenderRequests(renderer, RenderNodeCache.StableRequestCount + 2);

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

    private static void RenderRequests(RenderNodeRenderer renderer, int count)
    {
        for (int i = 0; i < count; i++)
        {
            using RenderNodeRasterization rasterization = renderer.Rasterize();
        }
    }

    private sealed class ThrowingRenderNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
            => throw new InvalidOperationException("recording failed");
    }

    private sealed class StatefulCallNode(Color color) : RenderNode
    {
        private static readonly Rect s_bounds = new(0, 0, 100, 100);
        private static readonly RenderResourceSlot<ExecutionProbe> s_probeSlot = new();
        private static readonly OpaqueRenderDefinition<Color> s_definition =
            OpaqueRenderDefinition<Color>.Create(
                static (session, state) => session.UseResource(s_probeSlot, probe =>
                {
                    probe.Record();
                    using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                    output.Canvas.Use(canvas => canvas.Clear(state));
                    session.Publish(output);
                }),
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.Vector,
                resources: [s_probeSlot]);

        private readonly ExecutionProbe _probe = new();
        private Color _color = color;

        public int ExecutionCount => _probe.Count;

        public void SetCallState(Color color, bool reportChanges)
        {
            _color = color;
            if (reportChanges)
                HasChanges = true;
        }

        public override void Process(RenderNodeContext context)
        {
            RenderResource<ExecutionProbe> probe = context.Borrow(_probe);
            context.Publish(context.OpaqueSource(s_definition.Call(
                _color,
                [s_probeSlot.Bind(probe)])));
        }
    }

    private sealed class ExecutionProbe
    {
        public int Count { get; private set; }

        public void Record() => Count++;
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        private static readonly SKColorSpace s_colorSpace = SKColorSpace.CreateSrgbLinear();

        public int GetMaximumDimension(RenderTargetAllocationDescriptor allocation)
            => RenderScaleUtilities.MaxBufferDimension;

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
