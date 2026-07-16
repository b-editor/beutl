using System.Collections.Immutable;
using System.Reflection;

using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.ProjectSystem;
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

            Assert.Multiple(() =>
            {
                Assert.That(ex!.Message, Is.EqualTo("fault"));
                Assert.That(disposed, Is.EquivalentTo(new[] { "first", "fault", "remaining" }));
                Assert.That(renderer.CurrentFramePullPurpose, Is.Null,
                    "a failed render must not publish the input frame as current");
            });
        });
    }

    [Test]
    public void UpdateFrame_DoesNotPublishPurpose_WhenDrawableUpdateThrows()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var renderer = new Renderer(16, 16, RenderIntent.Delivery);
            var validDrawable = new RoiAwareBoundsDrawable();
            using var validResource = (Drawable.Resource)validDrawable.ToResource(new CompositionContext(
                TimeSpan.Zero,
                RenderIntent.Delivery,
                RenderPullPurpose.Auxiliary));
            var validFrame = new CompositionFrame(
                ImmutableArray.Create<EngineObject.Resource>(validResource),
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(16, 16),
                RenderIntent.Delivery,
                RenderPullPurpose.Auxiliary);
            Assert.That(
                renderer.GetBoundary(validFrame, validDrawable),
                Is.EqualTo(new Rect(-8, 0, 16, 8)));
            Assert.That(renderer.CurrentFramePullPurpose, Is.EqualTo(RenderPullPurpose.Auxiliary));

            var drawable = new ThrowingFrameUpdateDrawable();
            using var resource = (Drawable.Resource)drawable.ToResource(new CompositionContext(
                TimeSpan.Zero,
                RenderIntent.Delivery,
                RenderPullPurpose.Auxiliary));
            var failingFrame = new CompositionFrame(
                ImmutableArray.Create<EngineObject.Resource>(validResource, resource),
                validFrame.Time,
                validFrame.Size,
                RenderIntent.Delivery,
                RenderPullPurpose.Auxiliary);

            InvalidOperationException? error = Assert.Throws<InvalidOperationException>(
                () => renderer.UpdateFrame(failingFrame));

            Assert.Multiple(() =>
            {
                Assert.That(error!.Message, Is.EqualTo("injected frame update failure"));
                Assert.That(renderer.CurrentFramePullPurpose, Is.Null,
                    "a failed update must invalidate, rather than publish, the partial render-node tree");
                Assert.That(renderer.GetBoundary(validDrawable), Is.Null,
                    "a clean entry added before the failure must not remain visible as the current frame");
                Assert.That(renderer.FindRenderNode(validDrawable), Is.Null,
                    "a failed frame must not expose an entry retained before the fault");
            });

            drawable.ThrowOnRender = false;
            Assert.DoesNotThrow(() => renderer.UpdateFrame(failingFrame));
            Assert.Multiple(() =>
            {
                Assert.That(drawable.RenderCalls, Is.EqualTo(2),
                    "retry must rebuild the faulted node tree instead of treating its partial resource as current");
                Assert.That(renderer.CurrentFramePullPurpose, Is.EqualTo(RenderPullPurpose.Auxiliary));
            });
        });
    }

    [Test]
    public void RenderDrawable_WhenDrawableRenderThrows_EvictsFaultedEntryBeforeRetry()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var drawable = new ThrowingFrameUpdateDrawable();
            using var resource = (Drawable.Resource)drawable.ToResource(new CompositionContext(
                TimeSpan.Zero,
                RenderIntent.Delivery,
                RenderPullPurpose.Frame));
            CompositionFrame frame = CreateFrame(resource);
            using var renderer = new Renderer(16, 16, RenderIntent.Delivery);

            InvalidOperationException? error = Assert.Throws<InvalidOperationException>(() => renderer.Render(frame));
            drawable.ThrowOnRender = false;
            Assert.DoesNotThrow(() => renderer.Render(frame));

            Assert.Multiple(() =>
            {
                Assert.That(error!.Message, Is.EqualTo("injected frame update failure"));
                Assert.That(drawable.RenderCalls, Is.EqualTo(2),
                    "retry must render into a new node tree after the first tree was left incomplete");
                Assert.That(renderer.CurrentFramePullPurpose, Is.EqualTo(RenderPullPurpose.Frame));
            });
        });
    }

    [Test]
    public void UpdateFrame_WhenRenderAndRetiredNodeCleanupThrow_PreservesRenderFailure()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var renderFailure = new InvalidOperationException("render callback failure");
            var cleanupFailure = new InvalidOperationException("retired node cleanup failure");
            var drawable = new DualFailureDrawable(renderFailure, cleanupFailure);
            using var resource = (Drawable.Resource)drawable.ToResource(new CompositionContext(
                TimeSpan.Zero, RenderIntent.Delivery, RenderPullPurpose.Auxiliary));
            CompositionFrame frame = CreateFrame(resource, RenderPullPurpose.Auxiliary);
            using var renderer = new Renderer(16, 16, RenderIntent.Delivery);
            renderer.UpdateFrame(frame);

            drawable.ThrowOnRender = true;
            drawable.Revision.CurrentValue++;
            bool updateOnly = false;
            resource.Update(drawable, new CompositionContext(
                TimeSpan.Zero, RenderIntent.Delivery, RenderPullPurpose.Auxiliary), ref updateOnly);

            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
                () => renderer.UpdateFrame(frame));

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(renderFailure));
                Assert.That(drawable.RetiredNodeDisposeCalls, Is.EqualTo(1));
                Assert.That(renderer.CurrentFramePullPurpose, Is.Null);
                Assert.That(renderer.FindRenderNode(drawable), Is.Null);
            });
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
                RenderPullPurpose.Auxiliary,
                CreateOperation("first", disposed),
                CreateOperation("fault", disposed, throwOnDispose: true),
                CreateOperation("remaining", disposed));
            using var renderer = new Renderer(16, 16, RenderIntent.Delivery);
            CompositionFrame frame = CreateFrame(resource, RenderPullPurpose.Auxiliary);

            var ex = Assert.Throws<InvalidOperationException>(() => renderer.RecalculateBoundaries(frame, 0));

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
            using var resource = (Drawable.Resource)drawable.ToResource(new CompositionContext(
                TimeSpan.Zero,
                RenderIntent.Delivery,
                RenderPullPurpose.Frame));
            using var renderer = new Renderer(16, 16, RenderIntent.Delivery);
            var frame = new CompositionFrame(
                ImmutableArray.Create<EngineObject.Resource>(resource),
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(16, 16),
                RenderIntent.Delivery,
                RenderPullPurpose.Frame);

            renderer.Render(frame);
            using var auxiliaryResource = (Drawable.Resource)drawable.ToResource(new CompositionContext(
                TimeSpan.Zero,
                RenderIntent.Delivery,
                RenderPullPurpose.Auxiliary));
            var auxiliaryFrame = new CompositionFrame(
                ImmutableArray.Create<EngineObject.Resource>(auxiliaryResource),
                frame.Time,
                frame.Size,
                RenderIntent.Delivery,
                RenderPullPurpose.Auxiliary);

            Assert.That(renderer.GetBoundary(auxiliaryFrame, drawable), Is.EqualTo(new Rect(-8, 0, 16, 8)));
        });
    }

    [Test]
    public void GetBoundary_AfterFullyCroppedRender_ReturnsOffscreenDrawableBounds()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var drawable = new FullyOffscreenRoiAwareBoundsDrawable();
            using var resource = (Drawable.Resource)drawable.ToResource(new CompositionContext(
                TimeSpan.Zero,
                RenderIntent.Delivery,
                RenderPullPurpose.Frame));
            using var renderer = new Renderer(16, 16, RenderIntent.Delivery);
            var frame = new CompositionFrame(
                ImmutableArray.Create<EngineObject.Resource>(resource),
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(16, 16),
                RenderIntent.Delivery,
                RenderPullPurpose.Frame);

            renderer.Render(frame);
            using var auxiliaryResource = (Drawable.Resource)drawable.ToResource(new CompositionContext(
                TimeSpan.Zero,
                RenderIntent.Delivery,
                RenderPullPurpose.Auxiliary));
            var auxiliaryFrame = new CompositionFrame(
                ImmutableArray.Create<EngineObject.Resource>(auxiliaryResource),
                frame.Time,
                frame.Size,
                RenderIntent.Delivery,
                RenderPullPurpose.Auxiliary);

            Assert.That(renderer.GetBoundary(auxiliaryFrame, drawable), Is.EqualTo(new Rect(-16, 0, 8, 8)));
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
                RenderPullPurpose.Auxiliary,
                CreateOperation("first", disposed, disposeFailure: cleanupFailure),
                CreateOperation(
                    "remaining", disposed,
                    disposeFailure: new InvalidOperationException("second cleanup failure")));
            using var renderer = new Renderer(16, 16, RenderIntent.Delivery);
            CompositionFrame frame = CreateFrame(resource, RenderPullPurpose.Auxiliary);

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
                RenderPullPurpose.Auxiliary,
                CreateOperation(
                    "first", disposed, hitTestFailure: hitTestFailure, disposeFailure: cleanupFailure),
                CreateOperation(
                    "remaining", disposed,
                    disposeFailure: new InvalidOperationException("second cleanup failure")));
            using var renderer = new Renderer(16, 16, RenderIntent.Delivery);
            CompositionFrame frame = CreateFrame(resource, RenderPullPurpose.Auxiliary);

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
            var compositionContext = new CompositionContext(
                TimeSpan.Zero, RenderIntent.Delivery, RenderPullPurpose.Frame);
            using var firstResource = first.ToResource(compositionContext);
            using var secondResource = second.ToResource(compositionContext);
            var resources = ImmutableArray.Create<EngineObject.Resource>(firstResource, secondResource);
            var frame = new CompositionFrame(
                resources,
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(16, 16),
                RenderIntent.Delivery,
                RenderPullPurpose.Frame);
            using var renderer = new Renderer(16, 16, RenderIntent.Delivery);
            renderer.UpdateFrame(frame);

            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(renderer.ClearAllCaches);

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(failure));
                Assert.That(disposed, Is.EquivalentTo(new[] { "first", "second" }),
                    "an entry disposal failure must not strand entries removed from the cache table");
                Assert.DoesNotThrow(renderer.ClearAllCaches, "the detached cache table must remain empty");
                Assert.That(renderer.CurrentFramePullPurpose, Is.Null);
                Assert.That(renderer.FindRenderNode(first), Is.Null,
                    "clearing caches must not leave a disposed entry published as the current frame");
                Assert.That(renderer.GetBoundary(first), Is.Null);
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

    [Test]
    public void Dispose_DetachesDrawableHierarchyHandlers()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var drawable = new RoiAwareBoundsDrawable();
            using var resource = (Drawable.Resource)drawable.ToResource(new CompositionContext(
                TimeSpan.Zero, RenderIntent.Delivery, RenderPullPurpose.Frame));
            var renderer = new Renderer(16, 16, RenderIntent.Delivery);
            renderer.UpdateFrame(CreateFrame(resource));
            FieldInfo eventField = typeof(Hierarchical).GetField(
                "DetachedFromHierarchy",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            Assert.That(((Delegate?)eventField.GetValue(drawable))?.GetInvocationList(), Has.Length.EqualTo(1));
            renderer.Dispose();

            Assert.That(((Delegate?)eventField.GetValue(drawable))?.GetInvocationList(), Is.Null.Or.Empty,
                "disposing a renderer must not leave its hierarchy handler rooted by a long-lived drawable");
        });
    }

    [Test]
    public void HierarchyDetach_WhenEntryCleanupThrows_UnpublishesCurrentEntryBeforeCleanup()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var cleanupFailure = new InvalidOperationException("detach cleanup failure");
            var disposed = new List<string>();
            var drawable = new DisposalTrackingDrawable("detached", disposed, cleanupFailure);
            string basePath = Path.Combine(Path.GetTempPath(), $"beutl_renderer_detach_{Guid.NewGuid():N}");
            var element = new Element
            {
                Uri = new Uri(Path.Combine(basePath, "element.layer"))
            };
            element.AddObject(drawable);
            var scene = new Scene(16, 16, string.Empty)
            {
                Uri = new Uri(Path.Combine(basePath, "scene.scene"))
            };
            scene.Children.Add(element);
            var hierarchyRoot = new BeutlApplication();
            hierarchyRoot.Items.Add(scene);
            using var resource = (Drawable.Resource)drawable.ToResource(new CompositionContext(
                TimeSpan.Zero, RenderIntent.Delivery, RenderPullPurpose.Frame));
            using var renderer = new Renderer(16, 16, RenderIntent.Delivery);
            renderer.UpdateFrame(CreateFrame(resource));

            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
                () => element.RemoveObject(drawable));

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(cleanupFailure));
                Assert.That(disposed, Is.EqualTo(new[] { "detached" }));
                Assert.That(renderer.FindRenderNode(drawable), Is.Null);
                Assert.That(renderer.GetBoundary(drawable), Is.Null,
                    "a partially disposed detached entry must never remain visible as current");
            });
        });
    }

    [Test]
    public void DisposedRenderer_RejectsWorkBeforeRecreatingNodeEntries()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var drawable = new ThrowingFrameUpdateDrawable { ThrowOnRender = false };
            using var resource = (Drawable.Resource)drawable.ToResource(new CompositionContext(
                TimeSpan.Zero, RenderIntent.Delivery, RenderPullPurpose.Frame));
            CompositionFrame frame = CreateFrame(resource);
            var renderer = new Renderer(16, 16, RenderIntent.Delivery);
            renderer.Dispose();

            Assert.Multiple(() =>
            {
                Assert.Throws<ObjectDisposedException>(() => renderer.Render(frame));
                Assert.Throws<ObjectDisposedException>(() => renderer.UpdateFrame(frame));
                Assert.Throws<ObjectDisposedException>(() => renderer.FindRenderNode(drawable));
                Assert.Throws<ObjectDisposedException>(() => renderer.Snapshot());
                Assert.That(drawable.RenderCalls, Is.Zero);
            });
        });
    }

    private static Drawable.Resource CreateResource(params RenderNodeOperation[] operations)
        => CreateResource(RenderPullPurpose.Frame, operations);

    private static Drawable.Resource CreateResource(
        RenderPullPurpose pullPurpose,
        params RenderNodeOperation[] operations)
    {
        var drawable = new FaultingDrawable(operations);
        return (Drawable.Resource)drawable.ToResource(new CompositionContext(
            TimeSpan.Zero,
            RenderIntent.Delivery,
            pullPurpose));
    }

    private static CompositionFrame CreateFrame(
        Drawable.Resource resource,
        RenderPullPurpose pullPurpose = RenderPullPurpose.Frame)
    {
        return new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new PixelSize(16, 16),
            RenderIntent.Delivery,
            pullPurpose);
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

internal sealed partial class ThrowingFrameUpdateDrawable : Drawable
{
    public bool ThrowOnRender { get; set; } = true;

    public int RenderCalls { get; private set; }

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        RenderCalls++;
        if (ThrowOnRender)
        {
            throw new InvalidOperationException("injected frame update failure");
        }
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(1, 1);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
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

internal sealed partial class DualFailureDrawable(
    Exception renderFailure,
    Exception retiredNodeCleanupFailure) : Drawable
{
    private readonly StableRenderNode _stableNode = new();
    private readonly ThrowingCleanupRenderNode _retiredNode = new(retiredNodeCleanupFailure);

    public IProperty<int> Revision { get; } = Property.Create(0);

    public bool ThrowOnRender { get; set; }

    public int RetiredNodeDisposeCalls => _retiredNode.DisposeCalls;

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        context.DrawNode(_stableNode);
        if (ThrowOnRender)
        {
            throw renderFailure;
        }

        context.DrawNode(_retiredNode);
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => Size.Empty;

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    private sealed class StableRenderNode : RenderNode
    {
        public override RenderNodeOperation[] Process(RenderNodeContext context) => [];
    }

    private sealed class ThrowingCleanupRenderNode(Exception failure) : RenderNode
    {
        public int DisposeCalls { get; private set; }

        public override RenderNodeOperation[] Process(RenderNodeContext context) => [];

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCalls++;
                throw failure;
            }
        }
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
