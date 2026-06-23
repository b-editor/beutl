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

    // RasterizeAt disposes the faulting op's render target via DisposeBestEffort, so a throwing
    // RenderTarget.Dispose() cannot mask the in-flight render exception.
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

    [Test]
    public void RasterizeToRenderTargets_DisposesCurrentAndRemainingOperations_WhenRenderTargetCreateThrows()
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("current", disposed),
            CreateOperation("remaining", disposed));
        var processor = new FakeRenderNodeProcessor(node, _ => false, throwOnCreate: true);

        var ex = Assert.Throws<InvalidOperationException>(() => processor.RasterizeToRenderTargets());

        Assert.That(ex!.Message, Is.EqualTo("rt-create-fault"));
        Assert.That(disposed, Is.EqualTo(new[] { "current", "remaining" }));
    }

    [Test]
    public void Rasterize_DisposesCurrentAndRemainingOperations_WhenRenderTargetCreateThrows()
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("current", disposed),
            CreateOperation("remaining", disposed));
        var processor = new FakeRenderNodeProcessor(node, _ => false, throwOnCreate: true);

        var ex = Assert.Throws<InvalidOperationException>(() => processor.Rasterize());

        Assert.That(ex!.Message, Is.EqualTo("rt-create-fault"));
        Assert.That(disposed, Is.EqualTo(new[] { "current", "remaining" }));
    }

    [Test]
    public void RasterizeAndConcat_DisposesPulledOperations_WhenRenderTargetCreateThrows()
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("first", disposed),
            CreateOperation("second", disposed));
        var processor = new FakeRenderNodeProcessor(node, _ => false, throwOnCreate: true);

        var ex = Assert.Throws<InvalidOperationException>(() => processor.RasterizeAndConcat());

        Assert.That(ex!.Message, Is.EqualTo("rt-create-fault"));
        Assert.That(disposed, Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public void RasterizeAndConcat_DisposesPulledOperations_WhenRenderTargetCreateReturnsNull()
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("first", disposed),
            CreateOperation("second", disposed));
        var processor = new FakeRenderNodeProcessor(node, _ => false, returnNullOnCreate: true);

        var ex = Assert.Throws<Exception>(() => processor.RasterizeAndConcat());

        Assert.That(ex!.Message, Is.EqualTo("RenderTarget is null"));
        Assert.That(disposed, Is.EqualTo(new[] { "first", "second" }));
    }

    // DisposeRenderTargets must keep sweeping past a list-resident RenderTarget that throws on
    // Dispose, without masking the render exception.
    [Test]
    public void RasterizeToRenderTargets_ContinuesCleanupAndPreservesException_WhenBuiltRenderTargetDisposeThrows()
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("first", disposed),                               // renders OK; its RT enters the list
            CreateOperation("second", disposed),                              // also list-resident
            CreateOperation("render-fault", disposed, throwOnRender: true));  // faults; its RT disposed in RasterizeAt
        // i => i == 0: only the first list-resident RT throws on Dispose, exercising the cleanup sweep.
        var processor = new FakeRenderNodeProcessor(node, i => i == 0);

        var ex = Assert.Throws<InvalidOperationException>(() => processor.RasterizeToRenderTargets());

        Assert.That(ex!.Message, Is.EqualTo("render-fault"));
        Assert.That(processor.CreatedTargets, Has.Count.EqualTo(3));
        Assert.That(processor.CreatedTargets[0].DisposeWasCalled, Is.True);
        Assert.That(processor.CreatedTargets[1].DisposeWasCalled, Is.True);
        Assert.That(processor.CreatedTargets[2].DisposeWasCalled, Is.True);
        Assert.That(disposed, Is.EqualTo(new[] { "first", "second", "render-fault" }));
    }

    // A throwing Dispose() during post-success cleanup must not discard the already-produced bitmap.
    [Test]
    public void RasterizeAndConcat_ReturnsBitmap_WhenRenderSucceedsButRenderTargetDisposeThrows()
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("ok", disposed));
        var processor = new FakeRenderNodeProcessor(node, _ => true);

        using var result = processor.RasterizeAndConcat();

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Width, Is.EqualTo(4));
        Assert.That(result.Height, Is.EqualTo(4));
        Assert.That(processor.CreatedTargets, Has.Count.EqualTo(1));
        Assert.That(processor.CreatedTargets[0].DisposeWasCalled, Is.True);
        Assert.That(disposed, Is.EqualTo(new[] { "ok" }));
    }

    // A throwing Dispose() during post-snapshot cleanup must not discard the already-snapshotted bitmaps.
    [Test]
    public void Rasterize_ReturnsBitmaps_WhenRenderSucceedsButRenderTargetDisposeThrows()
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("ok", disposed));
        var processor = new FakeRenderNodeProcessor(node, _ => true);

        var result = processor.Rasterize();

        Assert.That(result, Has.Count.EqualTo(1));
        using (result[0])
        {
            Assert.That(result[0].Width, Is.EqualTo(4));
            Assert.That(result[0].Height, Is.EqualTo(4));
        }

        Assert.That(processor.CreatedTargets, Has.Count.EqualTo(1));
        Assert.That(processor.CreatedTargets[0].DisposeWasCalled, Is.True);
        Assert.That(disposed, Is.EqualTo(new[] { "ok" }));
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
    // RenderTarget whose Dispose() throws.
    private sealed class FakeRenderNodeProcessor(
        RenderNode root,
        Func<int, bool> shouldThrowOnDispose,
        bool throwOnCreate = false,
        bool returnNullOnCreate = false)
        : RenderNodeProcessor(root, useRenderCache: false)
    {
        public List<FakeRenderTarget> CreatedTargets { get; } = new();

        protected override RenderTarget? CreateRenderTarget(int width, int height)
        {
            if (throwOnCreate)
            {
                throw new InvalidOperationException("rt-create-fault");
            }

            if (returnNullOnCreate)
            {
                return null;
            }

            var target = new FakeRenderTarget(width, height, shouldThrowOnDispose(CreatedTargets.Count));
            CreatedTargets.Add(target);
            return target;
        }
    }

    private sealed class FakeRenderTarget(int width, int height, bool throwOnDispose)
        : RenderTarget(CreateReadbackSurface(width, height), width, height)
    {
        public bool DisposeWasCalled { get; private set; }

        // A CPU raster surface (CreateNull has no backing store and fails ReadPixels) so the
        // RasterizeAndConcat success path can read pixels back through Snapshot() without a GPU.
        private static SKSurface CreateReadbackSurface(int width, int height) =>
            SKSurface.Create(new SKImageInfo(
                width, height, SKColorType.RgbaF16, SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear()));

        // Throw only on explicit disposal (disposing == true). The finalizer drives
        // Dispose(disposing: false), which must stay throw-free so a GC-collected double
        // cannot tear down the runtime from the finalizer thread.
        protected override void Dispose(bool disposing)
        {
            bool shouldThrow = disposing && throwOnDispose;
            if (disposing)
            {
                DisposeWasCalled = true;
            }

            base.Dispose(disposing);
            if (shouldThrow)
            {
                throw new InvalidOperationException("rt-dispose-fault");
            }
        }
    }
}
