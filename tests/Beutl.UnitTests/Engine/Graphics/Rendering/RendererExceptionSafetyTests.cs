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
    public void RenderDrawable_DisposesFaultingAndRemainingOperations_WhenRenderThrows()
    {
        VulkanTestEnvironment.EnsureAvailable();
        var disposed = new List<string>();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var renderer = new Renderer(16, 16, RenderIntent.Delivery);
            CompositionFrame frame = CreateFrame(
                CreateOperation("first", disposed),
                CreateOperation("fault", disposed, throwOnRender: true),
                CreateOperation("remaining", disposed));

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
            using var renderer = new Renderer(16, 16, RenderIntent.Delivery);
            CompositionFrame frame = CreateFrame(
                CreateOperation("first", disposed),
                CreateOperation("fault", disposed, throwOnDispose: true),
                CreateOperation("remaining", disposed));
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
            using var renderer = new Renderer(16, 16, RenderIntent.Delivery);
            var drawable = new RoiAwareBoundsDrawable();
            var resource = (Drawable.Resource)drawable.ToResource(CompositionContext.Default);
            var frame = new CompositionFrame(
                ImmutableArray.Create<EngineObject.Resource>(resource),
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(16, 16));

            renderer.Render(frame);

            Assert.That(renderer.GetBoundary(drawable), Is.EqualTo(new Rect(-8, 0, 16, 8)));
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

    private static CompositionFrame CreateFrame(params RenderNodeOperation[] operations)
    {
        var drawable = new FaultingDrawable(operations);
        var resource = (Drawable.Resource)drawable.ToResource(CompositionContext.Default);
        return new CompositionFrame(
            ImmutableArray.Create<EngineObject.Resource>(resource),
            new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            new PixelSize(16, 16));
    }

    private static RenderNodeOperation CreateOperation(
        string name,
        ICollection<string> disposed,
        bool throwOnRender = false,
        bool throwOnDispose = false)
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
            onDispose: () =>
            {
                disposed.Add(name);
                if (throwOnDispose)
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
