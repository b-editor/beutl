using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using SkiaSharp;

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

    // RasterizeAt's own catch disposes the faulting op AND its render target. Both go through
    // DisposeBestEffort, so a throwing op Dispose() there is swallowed and the original render
    // exception still propagates. Only Rasterize/RasterizeToRenderTargets reach RasterizeAt;
    // RasterizeAndConcat renders directly into a shared canvas and is covered above.
    [Test]
    public void RasterizeToRenderTargets_PreservesRenderException_WhenFaultingOpAlsoThrowsOnDispose()
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("first", disposed),
            CreateOperation("render-fault", disposed, throwOnRender: true, throwOnDispose: true, disposeFaultMessage: "dispose-fault"),
            CreateOperation("remaining", disposed));
        var processor = new RenderNodeProcessor(node, useRenderCache: false);

        var ex = Assert.Throws<InvalidOperationException>(() => processor.RasterizeToRenderTargets());

        Assert.That(ex!.Message, Is.EqualTo("render-fault"));
        Assert.That(disposed, Is.EqualTo(new[] { "first", "render-fault", "remaining" }));
    }

    [Test]
    public void Rasterize_PreservesRenderException_WhenFaultingOpAlsoThrowsOnDispose()
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("first", disposed),
            CreateOperation("render-fault", disposed, throwOnRender: true, throwOnDispose: true, disposeFaultMessage: "dispose-fault"),
            CreateOperation("remaining", disposed));
        var processor = new RenderNodeProcessor(node, useRenderCache: false);

        var ex = Assert.Throws<InvalidOperationException>(() => processor.Rasterize());

        Assert.That(ex!.Message, Is.EqualTo("render-fault"));
        Assert.That(disposed, Is.EqualTo(new[] { "first", "render-fault", "remaining" }));
    }

    [Test]
    public void Render_DisposesFaultingAndRemainingOperations_WhenRenderThrows()
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("first", disposed),
            CreateOperation("fault", disposed, throwOnRender: true),
            CreateOperation("remaining", disposed));
        var processor = new RenderNodeProcessor(node, useRenderCache: false);

        using var renderTarget = RenderTarget.Create(4, 4);
        Assume.That(renderTarget, Is.Not.Null);
        using var canvas = new ImmediateCanvas(renderTarget!);

        var ex = Assert.Throws<InvalidOperationException>(() => processor.Render(canvas));

        Assert.That(ex!.Message, Is.EqualTo("fault"));
        // A mid-loop render throw must still dispose the faulting op and every op after it,
        // or the GPU handles those ops back leak.
        Assert.That(disposed, Is.EquivalentTo(new[] { "first", "fault", "remaining" }));
    }

    [Test]
    public void Render_DoesNotDoubleDisposeFaultingOperation_WhenDisposeThrows()
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("first", disposed),
            CreateOperation("fault", disposed, throwOnDispose: true),
            CreateOperation("remaining", disposed));
        var processor = new RenderNodeProcessor(node, useRenderCache: false);

        using var renderTarget = RenderTarget.Create(4, 4);
        Assume.That(renderTarget, Is.Not.Null);
        using var canvas = new ImmediateCanvas(renderTarget!);

        var ex = Assert.Throws<InvalidOperationException>(() => processor.Render(canvas));

        Assert.That(ex!.Message, Is.EqualTo("fault"));
        // The faulting op must not be re-disposed by the sweep; the remaining op still gets cleaned up.
        Assert.That(disposed, Is.EquivalentTo(new[] { "first", "fault", "remaining" }));
    }

    [Test]
    public void DisposeAll_DisposesEveryOperation_EvenWhenAnOperationThrowsOnDispose()
    {
        var disposed = new List<string>();
        RenderNodeOperation[] ops =
        [
            CreateOperation("first", disposed),
            CreateOperation("throws", disposed, throwOnDispose: true),
            CreateOperation("remaining", disposed),
        ];

        // DisposeAll must swallow a faulting Dispose so the sweep reaches every trailing op.
        Assert.DoesNotThrow(() => RenderNodeOperation.DisposeAll(ops));
        Assert.That(disposed, Is.EquivalentTo(new[] { "first", "throws", "remaining" }));
    }

    // RasterizeAt routes the faulting op's render-target disposal through DisposeBestEffort, so a
    // throwing GPU-native RenderTarget.Dispose() is swallowed and cannot mask the in-flight render
    // exception. Only Rasterize / RasterizeToRenderTargets reach RasterizeAt's catch.
    [Test]
    public void RasterizeToRenderTargets_PreservesRenderException_WhenRenderTargetDisposeThrows()
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("render-fault", disposed, throwOnRender: true));
        var processor = new FakeRenderNodeProcessor(node, _ => true);

        var ex = Assert.Throws<InvalidOperationException>(() => processor.RasterizeToRenderTargets());

        Assert.That(ex!.Message, Is.EqualTo("render-fault"));
        Assert.That(processor.CreatedTargets, Has.Count.EqualTo(1));
        Assert.That(processor.CreatedTargets[0].DisposeWasCalled, Is.True);
        Assert.That(disposed, Is.EqualTo(new[] { "render-fault" }));
    }

    [Test]
    public void Rasterize_PreservesRenderException_WhenRenderTargetDisposeThrows()
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("render-fault", disposed, throwOnRender: true));
        var processor = new FakeRenderNodeProcessor(node, _ => true);

        var ex = Assert.Throws<InvalidOperationException>(() => processor.Rasterize());

        Assert.That(ex!.Message, Is.EqualTo("render-fault"));
        Assert.That(processor.CreatedTargets, Has.Count.EqualTo(1));
        Assert.That(processor.CreatedTargets[0].DisposeWasCalled, Is.True);
        Assert.That(disposed, Is.EqualTo(new[] { "render-fault" }));
    }

    [Test]
    public void RasterizeAndConcat_PreservesRenderException_WhenRenderTargetDisposeThrows()
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("render-fault", disposed, throwOnRender: true));
        var processor = new FakeRenderNodeProcessor(node, _ => true);

        var ex = Assert.Throws<InvalidOperationException>(() => processor.RasterizeAndConcat());

        Assert.That(ex!.Message, Is.EqualTo("render-fault"));
        Assert.That(processor.CreatedTargets, Has.Count.EqualTo(1));
        Assert.That(processor.CreatedTargets[0].DisposeWasCalled, Is.True);
        Assert.That(disposed, Is.EqualTo(new[] { "render-fault" }));
    }

    // The outer DisposeRenderTargets sweep must keep going and preserve the original render
    // exception when an already-built (list-resident) RenderTarget throws on Dispose during cleanup.
    [Test]
    public void RasterizeToRenderTargets_ContinuesCleanupAndPreservesException_WhenBuiltRenderTargetDisposeThrows()
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("first", disposed),                               // renders OK -> its RT enters the list
            CreateOperation("render-fault", disposed, throwOnRender: true));  // faults; its own RT is disposed in RasterizeAt
        // Only the first (list-resident) RT throws on Dispose; the faulting op's RT does not, so
        // the only throwing-dispose that fires here is the built-resource sweep.
        var processor = new FakeRenderNodeProcessor(node, i => i == 0);

        var ex = Assert.Throws<InvalidOperationException>(() => processor.RasterizeToRenderTargets());

        Assert.That(ex!.Message, Is.EqualTo("render-fault"));
        Assert.That(processor.CreatedTargets, Has.Count.EqualTo(2));
        Assert.That(processor.CreatedTargets[0].DisposeWasCalled, Is.True);   // swept despite throwing
        Assert.That(processor.CreatedTargets[1].DisposeWasCalled, Is.True);   // disposed in RasterizeAt catch
        Assert.That(disposed, Is.EqualTo(new[] { "first", "render-fault" }));
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
        bool throwOnDispose = false,
        string? disposeFaultMessage = null)
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
                    throw new InvalidOperationException(disposeFaultMessage ?? name);
                }
            });
    }

    private sealed class StaticRenderNode(params RenderNodeOperation[] operations) : RenderNode
    {
        public override RenderNodeOperation[] Process(RenderNodeContext context) => operations;
    }

    // Substitutes RenderTarget allocation so the exception-safety paths can be exercised with a
    // RenderTarget whose Dispose() throws. Backed by a null SKSurface so ImmediateCanvas can draw
    // into it without a GPU context.
    private sealed class FakeRenderNodeProcessor(RenderNode root, Func<int, bool> shouldThrowOnDispose)
        : RenderNodeProcessor(root, useRenderCache: false)
    {
        public List<FakeRenderTarget> CreatedTargets { get; } = new();

        protected override RenderTarget? CreateRenderTarget(int width, int height)
        {
            var target = new FakeRenderTarget(width, height, shouldThrowOnDispose(CreatedTargets.Count));
            CreatedTargets.Add(target);
            return target;
        }
    }

    private sealed class FakeRenderTarget(int width, int height, bool throwOnDispose)
        : RenderTarget(SKSurface.CreateNull(width, height), width, height)
    {
        public bool DisposeWasCalled { get; private set; }

        // Throw only on explicit disposal (disposing == true). The finalizer drives
        // Dispose(disposing: false), which must stay throw-free so a GC-collected double
        // cannot tear down the runtime from the finalizer thread.
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeWasCalled = true;
                if (throwOnDispose)
                {
                    throw new InvalidOperationException("rt-dispose-fault");
                }
            }

            base.Dispose(disposing);
        }
    }
}
