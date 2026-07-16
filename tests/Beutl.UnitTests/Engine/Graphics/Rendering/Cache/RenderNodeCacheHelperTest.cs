using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

public class RenderNodeCacheHelperTest
{
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
    public void CanCacheRecursiveChildrenOnly_ShouldReturnFalse_WhenAnyChildCannotCache()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);
        containerNode.Cache.ReportRenderCount(3);

        // Act
        var result = RenderNodeCacheHelper.CanCacheRecursiveChildrenOnly(containerNode);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CanCacheRecursiveChildrenOnly_ShouldReturnTrue_WhenAllChildrenCanCache()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);
        childNode.Cache.ReportRenderCount(3);

        // Act
        bool result = RenderNodeCacheHelper.CanCacheRecursiveChildrenOnly(containerNode);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void ClearCache_ShouldInvalidateCache()
    {
        // Arrange
        using var node = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        using (var renderTarget = RenderTarget.CreateNull(100, 100))
        {
            node.Cache.StoreCache(renderTarget, new Rect(0, 0, 100, 100));
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
        using (var renderTarget = RenderTarget.CreateNull(100, 100))
        {
            childNode.Cache.StoreCache(renderTarget, new Rect(0, 0, 100, 100));
        }
        node.AddChild(childNode);

        // Act
        RenderNodeCacheHelper.ClearCache(node);

        // Assert
        Assert.That(childNode.Cache.IsCached, Is.False);
    }

    [Test]
    public void ClearCache_WhenOneInvalidationThrows_StillSweepsItsDescendantsAndLaterSiblings()
    {
        using var root = new ContainerRenderNode();
        var failingContainer = new ContainerRenderNode();
        var grandchild = new EllipseRenderNode(new Rect(0, 0, 4, 4), Brushes.Resource.White, null);
        var laterSibling = new EllipseRenderNode(new Rect(0, 0, 4, 4), Brushes.Resource.White, null);
        failingContainer.AddChild(grandchild);
        root.AddChild(failingContainer);
        root.AddChild(laterSibling);
        using (var grandchildTarget = RenderTarget.CreateNull(4, 4))
        using (var siblingTarget = RenderTarget.CreateNull(4, 4))
        {
            grandchild.Cache.StoreCache(grandchildTarget, new Rect(0, 0, 4, 4));
            laterSibling.Cache.StoreCache(siblingTarget, new Rect(0, 0, 4, 4));
        }

        failingContainer.Cache.Dispose();

        Assert.Throws<ObjectDisposedException>(() => RenderNodeCacheHelper.ClearCache(root));

        Assert.Multiple(() =>
        {
            Assert.That(grandchild.Cache.IsCached, Is.False);
            Assert.That(laterSibling.Cache.IsCached, Is.False);
        });
    }

    [Test]
    public void MakeCache_ShouldCreateCache_WhenCacheIsEnabledAndCanCache()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);
        childNode.Cache.ReportRenderCount(3);
        containerNode.Cache.ReportRenderCount(3);
        var cacheOptions = new RenderCacheOptions(true, RenderCacheRules.Default);

        // Act
        RenderNodeCacheHelper.MakeCache(containerNode, cacheOptions, RenderIntent.Delivery);

        // Assert
        Assert.That(containerNode.Cache.IsCached, Is.True);
    }

    [Test]
    public void MakeCache_ShouldNotCreateCache_WhenCacheIsDisabled()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);
        childNode.Cache.ReportRenderCount(3);
        containerNode.Cache.ReportRenderCount(3);
        var cacheOptions = new RenderCacheOptions(false, RenderCacheRules.Default);

        // Act
        RenderNodeCacheHelper.MakeCache(containerNode, cacheOptions, RenderIntent.Delivery);

        // Assert
        Assert.That(containerNode.Cache.IsCached, Is.False);
    }

    [Test]
    public void MakeCache_ShouldNotCreateCache_WhenCannotCacheChildren()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);
        containerNode.Cache.ReportRenderCount(3);
        var cacheOptions = new RenderCacheOptions(true, RenderCacheRules.Default);

        // Act
        RenderNodeCacheHelper.MakeCache(containerNode, cacheOptions, RenderIntent.Delivery);

        // Assert
        Assert.That(containerNode.Cache.IsCached, Is.False);
    }

    [Test]
    public void CreateDefaultCache_ShouldStoreCache_WhenCacheRulesMatch()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);
        childNode.Cache.ReportRenderCount(3);
        containerNode.Cache.ReportRenderCount(3);
        var cacheOptions = new RenderCacheOptions(true, new RenderCacheRules(10000, 1));

        // Act
        RenderNodeCacheHelper.CreateDefaultCache(containerNode, cacheOptions, RenderIntent.Delivery);

        // Assert
        Assert.That(containerNode.Cache.IsCached, Is.True);
    }

    [Test]
    public void CreateDefaultCache_ShouldNotStoreCache_WhenCacheRulesDoNotMatch()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);
        var cacheOptions = new RenderCacheOptions(true, new RenderCacheRules(1, 1));

        // Act
        RenderNodeCacheHelper.CreateDefaultCache(containerNode, cacheOptions, RenderIntent.Delivery);

        // Assert
        Assert.That(containerNode.Cache.IsCached, Is.False);
    }

    [Test]
    public void CreateDefaultCache_NotificationFailure_RollsBackStoredTargets()
    {
        var injected = new InvalidOperationException("cache notification failed");
        using var node = new ThrowingCacheNotificationNode(injected);
        var cacheOptions = new RenderCacheOptions(true, new RenderCacheRules(100, 1));

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
            () => RenderNodeCacheHelper.CreateDefaultCache(node, cacheOptions, RenderIntent.Delivery));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(injected));
            Assert.That(node.Cache.IsCached, Is.False,
                "a failed notification must release the cache copies as well as the warm-up handles");
        });
    }

    [Test]
    public void CreateDefaultCache_HighDensityReject_SweepsOperationsAndThrowsFirstCleanupFailure()
    {
        var cleanupFailure = new InvalidOperationException("operation cleanup failure");
        var disposed = new List<string>();
        using var node = new HighDensityNode(disposed, cleanupFailure);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
            () => RenderNodeCacheHelper.CreateDefaultCache(
                node, RenderCacheOptions.Default, RenderIntent.Delivery, outputScale: 1f));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(cleanupFailure));
            Assert.That(disposed, Is.EqualTo(new[] { "first", "remaining" }));
            Assert.That(node.Cache.IsCacheRejected, Is.True,
                "cleanup failure must not leave a rejected cache eligible for another warm-up");
        });
    }

    [Test]
    public void CreateDefaultCache_EffectiveScaleFailure_SweepsEveryOperationAndPreservesPrimaryException()
    {
        var primary = new InvalidOperationException("effective-scale-fault");
        var disposed = new List<string>();
        using var node = new EffectiveScaleThrowingNode(primary, disposed);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
            () => RenderNodeCacheHelper.CreateDefaultCache(
                node, RenderCacheOptions.Default, RenderIntent.Delivery));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(primary));
            Assert.That(disposed, Is.EqualTo(new[] { "effective-scale", "remaining" }));
        });
    }

    [Test]
    [NonParallelizable]
    public void CreateDefaultCache_ClearCacheFailure_ReleasesNewRasterTargetsAndPreservesPrimaryException()
    {
        using var node = new TwoOperationNode();
        node.Cache.Dispose();
        int disposeCalls = 0;
        RenderNodeCacheHelper.SetRejectTargetDisposerForTest(target =>
        {
            disposeCalls++;
            target.Dispose();
            throw new InvalidOperationException("target-cleanup-fault");
        });

        try
        {
            Assert.Throws<ObjectDisposedException>(() => RenderNodeCacheHelper.CreateDefaultCache(
                node, RenderCacheOptions.Default, RenderIntent.Delivery));

            Assert.That(disposeCalls, Is.EqualTo(2));
        }
        finally
        {
            RenderNodeCacheHelper.SetRejectTargetDisposerForTest(null);
        }
    }

    [Test]
    [NonParallelizable]
    public void CreateDefaultCache_RuleReject_SweepsTargetsAndThrowsFirstCleanupFailure()
    {
        var cleanupFailure = new InvalidOperationException("target cleanup failure");
        using var node = new TwoOperationNode();
        int disposeCalls = 0;
        RenderNodeCacheHelper.SetRejectTargetDisposerForTest(target =>
        {
            disposeCalls++;
            target.Dispose();
            if (disposeCalls == 1)
            {
                throw cleanupFailure;
            }

            throw new InvalidOperationException("second target cleanup failure");
        });

        try
        {
            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
                () => RenderNodeCacheHelper.CreateDefaultCache(
                    node,
                    new RenderCacheOptions(true, new RenderCacheRules(1, 1)),
                    RenderIntent.Delivery));

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(cleanupFailure));
                Assert.That(disposeCalls, Is.EqualTo(2));
                Assert.That(node.Cache.IsCacheRejected, Is.True,
                    "cleanup failure must not leave a rejected cache eligible for another warm-up");
            });
        }
        finally
        {
            RenderNodeCacheHelper.SetRejectTargetDisposerForTest(null);
        }
    }

    [Test]
    public void PersistentCacheHelpers_RejectAuxiliaryBeforeAnyNodeOrCacheSideEffects()
    {
        using var makeNode = new CachePolicyProbeNode();
        using var createNode = new CachePolicyProbeNode();
        makeNode.Cache.ReportRenderCount(RenderNodeCache.Count);

        Assert.Multiple(() =>
        {
            Assert.Throws<NotSupportedException>(() => RenderNodeCacheHelper.MakeCache(
                makeNode,
                RenderCacheOptions.Default,
                RenderIntent.Preview,
                pullPurpose: RenderPullPurpose.Auxiliary));
            Assert.Throws<NotSupportedException>(() => RenderNodeCacheHelper.CreateDefaultCache(
                createNode,
                RenderCacheOptions.Default,
                RenderIntent.Preview,
                pullPurpose: RenderPullPurpose.Auxiliary));
        });

        Assert.Multiple(() =>
        {
            Assert.That(makeNode.ProcessCount, Is.Zero);
            Assert.That(makeNode.NotificationCount, Is.Zero);
            Assert.That(makeNode.Cache.IsCached, Is.False);
            Assert.That(makeNode.Cache.IsCacheRejected, Is.False);
            Assert.That(createNode.ProcessCount, Is.Zero);
            Assert.That(createNode.NotificationCount, Is.Zero);
            Assert.That(createNode.Cache.IsCached, Is.False);
            Assert.That(createNode.Cache.IsCacheRejected, Is.False);
        });
    }

    [Test]
    public void AuxiliaryProcessor_DoesNotReplayOrMutateFrameCache()
    {
        using var node = new CachePolicyProbeNode();
        RenderNodeCacheHelper.CreateDefaultCache(
            node,
            RenderCacheOptions.Default,
            RenderIntent.Preview);
        Assert.Multiple(() =>
        {
            Assert.That(node.Cache.CachedRenderIntent, Is.EqualTo(RenderIntent.Preview));
            Assert.That(node.Cache.CachedPullPurpose, Is.EqualTo(RenderPullPurpose.Frame));
        });
        node.ResetObservations();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        node.DisableRenderCacheOnProcess = true;

        PullAndDispose(node, RenderIntent.Preview, RenderPullPurpose.Auxiliary);

        Assert.Multiple(() =>
        {
            Assert.That(node.ProcessCount, Is.EqualTo(1),
                "an auxiliary processor must execute the node instead of replaying the persistent frame cache");
            Assert.That(node.ObservedPullPurpose, Is.EqualTo(RenderPullPurpose.Auxiliary));
            Assert.That(node.Cache.IsCached, Is.True,
                "an auxiliary pull must not invalidate or replace the retained frame cache");
            Assert.That(node.Cache.CanCache(), Is.True,
                "an auxiliary pull must not reset the frame cache stability counter");
            Assert.That(node.NotificationCount, Is.Zero,
                "an auxiliary pull must not notify nodes that retained frame state was served from cache");
        });
    }

    [Test]
    public void DeliveryProcessor_DoesNotReplayPreviewFrameCache()
    {
        using var node = new CachePolicyProbeNode();
        RenderNodeCacheHelper.CreateDefaultCache(
            node,
            RenderCacheOptions.Default,
            RenderIntent.Preview);
        node.ResetObservations();

        PullAndDispose(node, RenderIntent.Delivery, RenderPullPurpose.Frame);

        Assert.Multiple(() =>
        {
            Assert.That(node.ProcessCount, Is.EqualTo(1),
                "a delivery processor must not replay tiles produced under preview failure policy");
            Assert.That(node.ObservedRenderIntent, Is.EqualTo(RenderIntent.Delivery));
            Assert.That(node.ObservedPullPurpose, Is.EqualTo(RenderPullPurpose.Frame));
            Assert.That(node.Cache.IsCached, Is.True,
                "a policy mismatch must bypass, not destroy, the existing cache");
        });
    }

    [Test]
    public void PreviewRejection_DoesNotSuppressDeliveryWarmup()
    {
        using var node = new CachePolicyProbeNode { EffectiveScale = EffectiveScale.At(2f) };
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        RenderNodeCacheHelper.MakeCache(
            node,
            RenderCacheOptions.Default,
            RenderIntent.Preview,
            outputScale: 1f);
        Assert.Multiple(() =>
        {
            Assert.That(node.Cache.IsCacheRejected, Is.True, "sanity: preview warm-up must be rejected");
            Assert.That(node.Cache.IsCacheRejectedFor(RenderIntent.Preview), Is.True);
            Assert.That(node.Cache.IsCacheRejectedFor(RenderIntent.Delivery), Is.False,
                "the rejection memo must carry the exact render intent");
        });

        node.EffectiveScale = EffectiveScale.At(1f);
        node.ResetObservations();
        RenderNodeCacheHelper.MakeCache(
            node,
            RenderCacheOptions.Default,
            RenderIntent.Delivery,
            outputScale: 1f);

        Assert.Multiple(() =>
        {
            Assert.That(node.ProcessCount, Is.EqualTo(1),
                "a preview-only rejection must not suppress a delivery cache attempt");
            Assert.That(node.ObservedRenderIntent, Is.EqualTo(RenderIntent.Delivery));
            Assert.That(node.Cache.IsCached, Is.True);
        });
    }

    [Test]
    public void CacheEntryPoints_RejectUnknownPullPurposeBeforeSkippingWork()
    {
        using var node = new PullPurposeProbeNode();
        var invalidPurpose = (RenderPullPurpose)42;

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RenderNodeCacheHelper.MakeCache(
                node,
                RenderCacheOptions.Disabled,
                RenderIntent.Preview,
                pullPurpose: invalidPurpose));
            Assert.Throws<ArgumentOutOfRangeException>(() => RenderNodeCacheHelper.CreateDefaultCache(
                node,
                RenderCacheOptions.Default,
                RenderIntent.Preview,
                pullPurpose: invalidPurpose));
        });
    }

    private sealed class ThrowingCacheNotificationNode(Exception exception) : RenderNode
    {
        public override RenderNodeOperation[] Process(RenderNodeContext context)
        {
            var bounds = new Rect(0, 0, 10, 10);
            return
            [
                RenderNodeOperation.CreateLambda(
                    bounds,
                    canvas => canvas.DrawRectangle(bounds, Brushes.Resource.White, null)),
            ];
        }

        protected internal override void OnServedFromCache() => throw exception;
    }

    private sealed class HighDensityNode(ICollection<string> disposed, Exception cleanupFailure) : RenderNode
    {
        public override RenderNodeOperation[] Process(RenderNodeContext context)
        {
            var bounds = new Rect(0, 0, 4, 4);
            return
            [
                RenderNodeOperation.CreateLambda(
                    bounds,
                    static _ => { },
                    onDispose: () =>
                    {
                        disposed.Add("first");
                        throw cleanupFailure;
                    },
                    effectiveScale: EffectiveScale.At(2f)),
                RenderNodeOperation.CreateLambda(
                    bounds,
                    static _ => { },
                    onDispose: () =>
                    {
                        disposed.Add("remaining");
                        throw new InvalidOperationException("second operation cleanup failure");
                    },
                    effectiveScale: EffectiveScale.At(2f)),
            ];
        }
    }

    private sealed class TwoOperationNode : RenderNode
    {
        public override RenderNodeOperation[] Process(RenderNodeContext context)
        {
            var bounds = new Rect(0, 0, 4, 4);
            return
            [
                RenderNodeOperation.CreateLambda(bounds, static _ => { }),
                RenderNodeOperation.CreateLambda(bounds, static _ => { }),
            ];
        }
    }

    private sealed class EffectiveScaleThrowingNode(
        Exception exception,
        ICollection<string> disposed) : RenderNode
    {
        public override RenderNodeOperation[] Process(RenderNodeContext context)
        {
            return
            [
                new EffectiveScaleThrowingOperation(exception, "effective-scale", disposed),
                RenderNodeOperation.CreateLambda(
                    new Rect(0, 0, 4, 4),
                    static _ => { },
                    onDispose: () =>
                    {
                        disposed.Add("remaining");
                        throw new InvalidOperationException("remaining-cleanup-fault");
                    }),
            ];
        }
    }

    private sealed class EffectiveScaleThrowingOperation(
        Exception exception,
        string name,
        ICollection<string> disposed) : RenderNodeOperation
    {
        public override Rect Bounds => new(0, 0, 4, 4);

        public override EffectiveScale EffectiveScale => throw exception;

        public override void Render(ImmediateCanvas canvas)
        {
        }

        public override bool HitTest(Point point) => false;

        protected override void OnDispose(bool disposing)
        {
            disposed.Add(name);
            throw new InvalidOperationException("effective-scale-cleanup-fault");
        }
    }

    private sealed class PullPurposeProbeNode : RenderNode
    {
        public RenderPullPurpose? ObservedPullPurpose { get; private set; }

        public override RenderNodeOperation[] Process(RenderNodeContext context)
        {
            ObservedPullPurpose = context.PullPurpose;
            return
            [
                RenderNodeOperation.CreateLambda(
                    new Rect(0, 0, 1, 1),
                    static _ => { },
                    effectiveScale: EffectiveScale.At(2f)),
            ];
        }
    }

    private sealed class CachePolicyProbeNode : RenderNode
    {
        public int ProcessCount { get; private set; }

        public int NotificationCount { get; private set; }

        public RenderIntent? ObservedRenderIntent { get; private set; }

        public RenderPullPurpose? ObservedPullPurpose { get; private set; }

        public EffectiveScale EffectiveScale { get; set; } = EffectiveScale.At(1f);

        public bool DisableRenderCacheOnProcess { get; set; }

        public void ResetObservations()
        {
            ProcessCount = 0;
            NotificationCount = 0;
            ObservedRenderIntent = null;
            ObservedPullPurpose = null;
        }

        public override RenderNodeOperation[] Process(RenderNodeContext context)
        {
            ProcessCount++;
            ObservedRenderIntent = context.RenderIntent;
            ObservedPullPurpose = context.PullPurpose;
            if (DisableRenderCacheOnProcess)
            {
                context.IsRenderCacheEnabled = false;
            }
            return
            [
                RenderNodeOperation.CreateLambda(
                    new Rect(0, 0, 4, 4),
                    static _ => { },
                    effectiveScale: EffectiveScale),
            ];
        }

        protected internal override void OnServedFromCache()
        {
            NotificationCount++;
        }
    }

    private static void PullAndDispose(
        RenderNode node,
        RenderIntent renderIntent,
        RenderPullPurpose pullPurpose)
    {
        var processor = new RenderNodeProcessor(
            node,
            useRenderCache: true,
            renderIntent,
            pullPurpose: pullPurpose);
        RenderNodeOperation.DisposeAll(processor.PullToRoot());
    }
}
