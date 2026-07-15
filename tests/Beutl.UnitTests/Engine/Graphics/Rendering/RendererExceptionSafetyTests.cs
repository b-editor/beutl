using System.Collections.Immutable;

using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// Drives the cleanup-sweep contract through a real Renderer (RenderDrawable and RecalculateBoundaries):
// a mid-loop throw must still dispose every pulled op, or the GPU handles they back leak. Renderer needs
// Vulkan and the render thread, hence VulkanTestEnvironment.
[NonParallelizable]
[TestFixture]
public class RendererExceptionSafetyTests
{
    [Test]
    public void Renderer_DoesNotSynchronouslyDisposeOnTheFinalizerThread()
    {
        System.Reflection.MethodInfo finalize = typeof(Renderer).GetMethod(
            "Finalize",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        Assert.That(finalize.DeclaringType, Is.Not.EqualTo(typeof(Renderer)),
            "renderer teardown is render-thread-affine and must remain an explicit Dispose operation");
    }

    [Test]
    public void RenderDrawable_DisposesFaultingAndRemainingOperations_WhenRenderThrows()
    {
        VulkanTestEnvironment.EnsureAvailable();
        var disposed = new List<string>();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var resource = CreateResource(
                CreateOperation("first", disposed),
                CreateOperation("fault", disposed, throwOnRender: true),
                CreateOperation("remaining", disposed));
            using var renderer = new Renderer(16, 16, RenderIntent.Delivery);
            CompositionFrame frame = CreateFrame(resource);

            var ex = Assert.Throws<InvalidOperationException>(() => renderer.Render(frame));

            Assert.That(ex!.Message, Is.EqualTo("fault"));
            Assert.That(disposed, Is.EquivalentTo(new[] { "first", "fault", "remaining" }));
        });
    }

    [Test]
    public void RecalculateBoundaries_DisposesFaultingAndRemainingOperations_WhenDisposeThrows()
    {
        VulkanTestEnvironment.EnsureAvailable();
        var disposed = new List<string>();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var resource = CreateResource(
                CreateOperation("first", disposed),
                CreateOperation("fault", disposed, throwOnDispose: true),
                CreateOperation("remaining", disposed));
            using var renderer = new Renderer(16, 16, RenderIntent.Delivery);
            CompositionFrame frame = CreateFrame(resource);
            renderer.UpdateFrame(frame);

            var ex = Assert.Throws<InvalidOperationException>(() => renderer.RecalculateBoundaries(0));

            Assert.That(ex!.Message, Is.EqualTo("fault"));
            // The faulting op must not be re-disposed; the trailing op must still be cleaned up by the sweep.
            Assert.That(disposed, Is.EquivalentTo(new[] { "first", "fault", "remaining" }));
        });
    }

    [Test]
    public void GetBoundary_AfterCroppedRender_ReturnsUncroppedDrawableBounds()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var drawable = new RoiAwareBoundsDrawable();
            using var resource = (Drawable.Resource)drawable.ToResource(CompositionContext.Default);
            using var renderer = new Renderer(16, 16, RenderIntent.Delivery);
            var frame = new CompositionFrame(
                ImmutableArray.Create<EngineObject.Resource>(resource),
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(16, 16));

            renderer.Render(frame);

            Assert.That(renderer.GetBoundary(drawable), Is.EqualTo(new Rect(-8, 0, 16, 8)));
        });
    }

    [Test]
    public void GetBoundary_AfterFullyCroppedRender_ReturnsOffscreenDrawableBounds()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var drawable = new FullyOffscreenRoiAwareBoundsDrawable();
            using var resource = (Drawable.Resource)drawable.ToResource(CompositionContext.Default);
            using var renderer = new Renderer(16, 16, RenderIntent.Delivery);
            var frame = new CompositionFrame(
                ImmutableArray.Create<EngineObject.Resource>(resource),
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(16, 16));

            renderer.Render(frame);

            Assert.That(renderer.GetBoundary(drawable), Is.EqualTo(new Rect(-16, 0, 8, 8)));
        });
    }

    [Test]
    public void HitTest_ContinuesCleanupAndThrowsFirstCleanupFailure_WhenHitTestSucceeds()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var cleanupFailure = new InvalidOperationException("first cleanup failure");
            var disposed = new List<string>();
            using var resource = CreateResource(
                CreateOperation("first", disposed, disposeFailure: cleanupFailure),
                CreateOperation(
                    "remaining", disposed,
                    disposeFailure: new InvalidOperationException("second cleanup failure")));
            using var renderer = new Renderer(16, 16, RenderIntent.Delivery);
            CompositionFrame frame = CreateFrame(resource);

            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
                () => renderer.HitTest(frame, new Point(15, 15)));

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(cleanupFailure));
                Assert.That(disposed, Is.EqualTo(new[] { "first", "remaining" }));
            });
        });
    }

    [Test]
    public void HitTest_PreservesHitTestFailure_WhenCleanupAlsoThrows()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var hitTestFailure = new InvalidOperationException("hit-test failure");
            var cleanupFailure = new InvalidOperationException("cleanup failure");
            var disposed = new List<string>();
            using var resource = CreateResource(
                CreateOperation(
                    "first", disposed, hitTestFailure: hitTestFailure, disposeFailure: cleanupFailure),
                CreateOperation(
                    "remaining", disposed,
                    disposeFailure: new InvalidOperationException("second cleanup failure")));
            using var renderer = new Renderer(16, 16, RenderIntent.Delivery);
            CompositionFrame frame = CreateFrame(resource);

            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
                () => renderer.HitTest(frame, new Point(0, 0)));

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(hitTestFailure));
                Assert.That(disposed, Is.EqualTo(new[] { "first", "remaining" }));
            });
        });
    }

    [Test]
    public void ClearAllCaches_ContinuesEntrySweepAndThrowsFirstDisposalFailure()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var failure = new InvalidOperationException("entry disposal failure");
            var disposed = new List<string>();
            var first = new DisposalTrackingDrawable("first", disposed, failure);
            var second = new DisposalTrackingDrawable("second", disposed, failure);
            using var firstResource = first.ToResource(CompositionContext.Default);
            using var secondResource = second.ToResource(CompositionContext.Default);
            var resources = ImmutableArray.Create<EngineObject.Resource>(firstResource, secondResource);
            var frame = new CompositionFrame(
                resources,
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(16, 16));
            using var renderer = new Renderer(16, 16, RenderIntent.Delivery);
            renderer.UpdateFrame(frame);

            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(renderer.ClearAllCaches);

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(failure));
                Assert.That(disposed, Is.EquivalentTo(new[] { "first", "second" }),
                    "an entry disposal failure must not strand entries removed from the cache table");
                Assert.DoesNotThrow(renderer.ClearAllCaches, "the detached cache table must remain empty");
            });
        });
    }

    [Test]
    public void Dispose_OnDisposeThrows_StillReleasesRendererResources()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var renderer = new ThrowingDisposeRenderer(16, 16);
            ImmediateCanvas canvas = Renderer.GetInternalCanvas(renderer);
            RenderTarget target = Renderer.GetInternalRenderTarget(renderer);

            InvalidOperationException? error = Assert.Throws<InvalidOperationException>(renderer.Dispose);

            Assert.Multiple(() =>
            {
                Assert.That(error!.Message, Is.EqualTo("injected renderer disposal failure"));
                Assert.That(renderer.IsDisposed, Is.True);
                Assert.That(canvas.IsDisposed, Is.True, "canvas teardown must run after OnDispose fails");
                Assert.That(target.IsDisposed, Is.True, "surface teardown must run after OnDispose fails");
                Assert.DoesNotThrow(renderer.Dispose, "a completed cleanup remains idempotent");
            });
        });
    }

    private static Drawable.Resource CreateResource(params RenderNodeOperation[] operations)
    {
        var drawable = new FaultingDrawable(operations);
        return (Drawable.Resource)drawable.ToResource(CompositionContext.Default);
    }

    private static CompositionFrame CreateFrame(Drawable.Resource resource)
    {
        return new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new PixelSize(16, 16));
    }

    private static RenderNodeOperation CreateOperation(
        string name,
        ICollection<string> disposed,
        bool throwOnRender = false,
        bool throwOnDispose = false,
        Exception? hitTestFailure = null,
        Exception? disposeFailure = null)
    {
        return RenderNodeOperation.CreateLambda(
            new Rect(0, 0, 4, 4),
            _ =>
            {
                if (throwOnRender)
                {
                    throw new InvalidOperationException(name);
                }
            },
            hitTest: _ => hitTestFailure == null ? false : throw hitTestFailure,
            onDispose: () =>
            {
                disposed.Add(name);
                if (disposeFailure != null)
                {
                    throw disposeFailure;
                }
                else if (throwOnDispose)
                {
                    throw new InvalidOperationException(name);
                }
            });
    }
}

internal sealed class ThrowingDisposeRenderer(int width, int height)
    : Renderer(width, height, RenderIntent.Delivery)
{
    protected override void OnDispose(bool disposing)
        => throw new InvalidOperationException("injected renderer disposal failure");
}

// Emits a fixed set of ops into the render graph, with Render overridden to bypass the blend/opacity/filter
// pushes so they reach the Renderer's pull loop unwrapped. Top-level partial because
// EngineObjectResourceGenerator does not support nested types.
internal sealed partial class FaultingDrawable(RenderNodeOperation[] operations) : Drawable
{
    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
        => context.DrawNode(new FixedOpsNode(operations));

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(4, 4);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}

internal sealed class FixedOpsNode(RenderNodeOperation[] operations) : RenderNode
{
    public override RenderNodeOperation[] Process(RenderNodeContext context) => operations;
}

internal sealed partial class RoiAwareBoundsDrawable : Drawable
{
    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
        => context.DrawNode(new RoiAwareBoundsNode());

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(16, 8);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}

internal sealed class RoiAwareBoundsNode : RenderNode
{
    private static readonly Rect s_bounds = new(-8, 0, 16, 8);

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        Rect bounds = context.RequestedBounds.IsInvalid
            ? s_bounds
            : s_bounds.Intersect(context.RequestedBounds);
        return [RenderNodeOperation.CreateLambda(bounds, static _ => { })];
    }
}

internal sealed partial class FullyOffscreenRoiAwareBoundsDrawable : Drawable
{
    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
        => context.DrawNode(new FullyOffscreenRoiAwareBoundsNode());

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(8, 8);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}

internal sealed class FullyOffscreenRoiAwareBoundsNode : RenderNode
{
    private static readonly Rect s_bounds = new(-16, 0, 8, 8);

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        Rect bounds = context.RequestedBounds.IsInvalid
            ? s_bounds
            : s_bounds.Intersect(context.RequestedBounds);
        return [RenderNodeOperation.CreateLambda(bounds, static _ => { })];
    }
}

internal sealed partial class DisposalTrackingDrawable(
    string name,
    ICollection<string> disposed,
    Exception failure) : Drawable
{
    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
        => context.DrawNode(new DisposalTrackingNode(name, disposed, failure));

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(1, 1);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}

internal sealed class DisposalTrackingNode(
    string name,
    ICollection<string> disposed,
    Exception failure) : RenderNode
{
    private bool _disposeAttempted;

    public override RenderNodeOperation[] Process(RenderNodeContext context) => [];

    protected override void OnDispose(bool disposing)
    {
        if (disposing && !_disposeAttempted)
        {
            _disposeAttempted = true;
            disposed.Add(name);
            throw failure;
        }
    }
}
