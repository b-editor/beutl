using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class RenderNodeProcessorExceptionSafetyTests
{
    [Test]
    public void Rasterize_DisposesFaultingAndRemainingOperations_WhenRenderThrows()
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("first", disposed),
            CreateOperation("fault", disposed, throwOnRender: true),
            CreateOperation("remaining", disposed));
        var processor = new RenderNodeProcessor(node, useRenderCache: false);

        var ex = Assert.Throws<InvalidOperationException>(() => processor.Rasterize());

        Assert.That(ex!.Message, Is.EqualTo("fault"));
        Assert.That(disposed, Is.EquivalentTo(new[] { "first", "fault", "remaining" }));
    }

    [Test]
    public void RasterizeAndConcat_DisposesFaultingAndRemainingOperations_WhenRenderThrows()
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("first", disposed),
            CreateOperation("fault", disposed, throwOnRender: true),
            CreateOperation("remaining", disposed));
        var processor = new RenderNodeProcessor(node, useRenderCache: false);

        var ex = Assert.Throws<InvalidOperationException>(() => processor.RasterizeAndConcat());

        Assert.That(ex!.Message, Is.EqualTo("fault"));
        Assert.That(disposed, Is.EquivalentTo(new[] { "first", "fault", "remaining" }));
    }

    [Test]
    public void RasterizeAndConcat_DoesNotDoubleDisposeFaultingOperation_WhenDisposeThrows()
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("first", disposed),
            CreateOperation("fault", disposed, throwOnDispose: true),
            CreateOperation("remaining", disposed));
        var processor = new RenderNodeProcessor(node, useRenderCache: false);

        var ex = Assert.Throws<InvalidOperationException>(() => processor.RasterizeAndConcat());

        // The propagated exception must be the faulting op's Dispose, not a masked second throw.
        Assert.That(ex!.Message, Is.EqualTo("fault"));
        // The faulting op must be disposed exactly once, and the remaining op must still be
        // cleaned up: a double-dispose re-runs OnDispose (use-after-free for GPU-backed ops)
        // and skips the remaining op.
        Assert.That(disposed, Is.EquivalentTo(new[] { "first", "fault", "remaining" }));
    }

    [Test]
    public void Rasterize_DoesNotDoubleDisposeFaultingOperation_WhenDisposeThrows()
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("first", disposed),
            CreateOperation("fault", disposed, throwOnDispose: true),
            CreateOperation("remaining", disposed));
        var processor = new RenderNodeProcessor(node, useRenderCache: false);

        // Rasterize delegates per-op disposal to RasterizeAt, which used to dispose the op once in
        // its try and again in its catch — re-running a throwing OnDispose (use-after-free).
        var ex = Assert.Throws<InvalidOperationException>(() => processor.Rasterize());

        Assert.That(ex!.Message, Is.EqualTo("fault"));
        Assert.That(disposed, Is.EquivalentTo(new[] { "first", "fault", "remaining" }));
    }

    [Test]
    public void RasterizeToRenderTargets_ContinuesCleanupAndPreservesOriginalException_WhenSweepDisposeThrows()
    {
        var disposed = new List<string>();
        using var node = CreateRenderThrowWithThrowingRemainingOps(disposed);
        var processor = new RenderNodeProcessor(node, useRenderCache: false);

        var ex = Assert.Throws<InvalidOperationException>(() => processor.RasterizeToRenderTargets());

        Assert.That(ex!.Message, Is.EqualTo("render-fault"));
        Assert.That(disposed, Is.EqualTo(new[]
        {
            "first",
            "render-fault",
            "throwing-remaining-1",
            "throwing-remaining-2",
            "remaining"
        }));
    }

    [Test]
    public void Rasterize_ContinuesCleanupAndPreservesOriginalException_WhenSweepDisposeThrows()
    {
        var disposed = new List<string>();
        using var node = CreateRenderThrowWithThrowingRemainingOps(disposed);
        var processor = new RenderNodeProcessor(node, useRenderCache: false);

        var ex = Assert.Throws<InvalidOperationException>(() => processor.Rasterize());

        Assert.That(ex!.Message, Is.EqualTo("render-fault"));
        Assert.That(disposed, Is.EqualTo(new[]
        {
            "first",
            "render-fault",
            "throwing-remaining-1",
            "throwing-remaining-2",
            "remaining"
        }));
    }

    [Test]
    public void RasterizeAndConcat_ContinuesCleanupAndPreservesOriginalException_WhenSweepDisposeThrows()
    {
        var disposed = new List<string>();
        using var node = CreateRenderThrowWithThrowingRemainingOps(disposed);
        var processor = new RenderNodeProcessor(node, useRenderCache: false);

        var ex = Assert.Throws<InvalidOperationException>(() => processor.RasterizeAndConcat());

        Assert.That(ex!.Message, Is.EqualTo("render-fault"));
        Assert.That(disposed, Is.EqualTo(new[]
        {
            "first",
            "render-fault",
            "throwing-remaining-1",
            "throwing-remaining-2",
            "remaining"
        }));
    }

    private static StaticRenderNode CreateRenderThrowWithThrowingRemainingOps(ICollection<string> disposed)
    {
        return new StaticRenderNode(
            CreateOperation("first", disposed),
            CreateOperation("render-fault", disposed, throwOnRender: true),
            CreateOperation("throwing-remaining-1", disposed, throwOnDispose: true),
            CreateOperation("throwing-remaining-2", disposed, throwOnDispose: true),
            CreateOperation("remaining", disposed));
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

    private sealed class StaticRenderNode(params RenderNodeOperation[] operations) : RenderNode
    {
        public override RenderNodeOperation[] Process(RenderNodeContext context) => operations;
    }
}
