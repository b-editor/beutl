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
            using var renderer = new Renderer(16, 16);
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
            using var renderer = new Renderer(16, 16);
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
    public void RenderDrawable_RebuildsCachedNode_AfterTransientRenderFailure()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var renderer = new Renderer(16, 16);
            var drawable = new TransientFaultDrawable();
            CompositionFrame frame = CreateFrame(drawable);

            var first = Assert.Throws<InvalidOperationException>(() => renderer.Render(frame));

            Assert.That(first!.Message, Is.EqualTo("transient"));
            Assert.That(() => renderer.Render(frame), Throws.Nothing);
            Assert.That(drawable.RenderCount, Is.EqualTo(2));
        });
    }

    private static CompositionFrame CreateFrame(params RenderNodeOperation[] operations)
    {
        var drawable = new FaultingDrawable(operations);
        return CreateFrame(drawable);
    }

    private static CompositionFrame CreateFrame(Drawable drawable)
    {
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

internal sealed partial class TransientFaultDrawable : Drawable
{
    public int RenderCount { get; private set; }

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        RenderCount++;
        bool shouldThrow = RenderCount == 1;
        context.DrawNode(new TransientFaultNode(shouldThrow));
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(4, 4);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}

internal sealed class TransientFaultNode(bool shouldThrow) : RenderNode
{
    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        return
        [
            RenderNodeOperation.CreateLambda(
                new Rect(0, 0, 4, 4),
                _ =>
                {
                    if (shouldThrow)
                    {
                        throw new InvalidOperationException("transient");
                    }
                })
        ];
    }
}
