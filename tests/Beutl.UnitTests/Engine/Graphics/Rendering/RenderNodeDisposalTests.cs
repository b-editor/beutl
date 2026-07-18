using System.Reflection;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class RenderNodeDisposalTests
{
    [Test]
    public void Dispose_WhenNodeCleanupThrows_MarksDisposedSweepsCacheAndDoesNotRetry()
    {
        var cleanupFailure = new InvalidOperationException("node cleanup failure");
        var node = new ThrowingDisposeRenderNode(cleanupFailure);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(node.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(cleanupFailure));
            Assert.That(node.IsDisposedDuringCleanup, Is.False,
                "existing overrides may guard their cleanup with IsDisposed");
            Assert.That(node.IsDisposed, Is.True);
            Assert.That(node.Cache.IsDisposed, Is.True);
            Assert.That(node.DisposeCalls, Is.EqualTo(1));
            Assert.DoesNotThrow(node.Dispose);
            Assert.That(node.DisposeCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void Finalizer_SuppressesNodeCleanupFailure()
    {
        var node = new ThrowingDisposeRenderNode(new InvalidOperationException("finalizer cleanup failure"));
        MethodInfo finalizer = typeof(RenderNode).GetMethod(
            "Finalize",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        try
        {
            Assert.DoesNotThrow(() => finalizer.Invoke(node, null));
            Assert.Multiple(() =>
            {
                Assert.That(node.IsDisposed, Is.True);
                Assert.That(node.DisposeCalls, Is.EqualTo(1));
                Assert.That(node.LastDisposing, Is.False);
                Assert.That(node.IsDisposedDuringCleanup, Is.False);
            });
        }
        finally
        {
            GC.SuppressFinalize(node);
        }
    }

    [Test]
    public void RenderNodeOperation_WhenCleanupThrows_MarksDisposedAndDoesNotRetry()
    {
        int disposeCalls = 0;
        bool? isDisposedDuringCleanup = null;
        var cleanupFailure = new InvalidOperationException("operation cleanup failure");
        RenderNodeOperation operation = null!;
        operation = RenderNodeOperation.CreateLambda(
            Rect.Empty,
            static _ => { },
            onDispose: () =>
            {
                isDisposedDuringCleanup = operation.IsDisposed;
                disposeCalls++;
                throw cleanupFailure;
            });

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(operation.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(cleanupFailure));
            Assert.That(isDisposedDuringCleanup, Is.False,
                "existing operations may guard their cleanup with IsDisposed");
            Assert.That(operation.IsDisposed, Is.True);
            Assert.That(disposeCalls, Is.EqualTo(1));
            Assert.DoesNotThrow(operation.Dispose);
            Assert.That(disposeCalls, Is.EqualTo(1));
        });
    }

    [Test]
    public void DisposedRenderNodeCache_RejectsStateMutationAndReplay()
    {
        var node = new ContainerRenderNode();
        RenderNodeCache cache = node.Cache;
        node.Dispose();

        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => cache.ReportRenderCount(1));
            Assert.Throws<ObjectDisposedException>(cache.IncrementRenderCount);
            Assert.Throws<ObjectDisposedException>(cache.RejectCache);
            Assert.Throws<ObjectDisposedException>(cache.Invalidate);
            Assert.Throws<ObjectDisposedException>(() => cache.UseCache(out _));
            Assert.Throws<ObjectDisposedException>(() => cache.StoreCache(
                Array.Empty<(RenderTarget RenderTarget, Rect Bounds)>(),
                RenderIntent.Delivery));
        });
    }

    [Test]
    public void DecoratorOperation_WhenBothCleanupStepsThrow_PreservesChildFailureAndSweepsCallback()
    {
        var childFailure = new InvalidOperationException("child cleanup failure");
        var callbackFailure = new InvalidOperationException("callback cleanup failure");
        int childCleanupCalls = 0;
        int callbackCleanupCalls = 0;
        RenderNodeOperation child = RenderNodeOperation.CreateLambda(
            Rect.Empty,
            static _ => { },
            onDispose: () =>
            {
                childCleanupCalls++;
                throw childFailure;
            });
        RenderNodeOperation decorator = RenderNodeOperation.CreateDecorator(
            child,
            static _ => { },
            onDispose: () =>
            {
                callbackCleanupCalls++;
                throw callbackFailure;
            });

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(decorator.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(childFailure));
            Assert.That(childCleanupCalls, Is.EqualTo(1));
            Assert.That(callbackCleanupCalls, Is.EqualTo(1));
            Assert.That(child.IsDisposed, Is.True);
            Assert.That(decorator.IsDisposed, Is.True);
        });
    }

    private sealed class ThrowingDisposeRenderNode(Exception cleanupFailure) : RenderNode
    {
        public int DisposeCalls { get; private set; }

        public bool? LastDisposing { get; private set; }

        public bool? IsDisposedDuringCleanup { get; private set; }

        public override RenderNodeOperation[] Process(RenderNodeContext context) => [];

        protected override void OnDispose(bool disposing)
        {
            IsDisposedDuringCleanup = IsDisposed;
            DisposeCalls++;
            LastDisposing = disposing;
            throw cleanupFailure;
        }
    }
}
