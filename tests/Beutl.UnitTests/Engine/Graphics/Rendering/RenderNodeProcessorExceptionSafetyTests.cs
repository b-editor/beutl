using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class RenderNodeProcessorExceptionSafetyTests
{
    // The three rasterize entry points share one disposal contract, so every scenario below runs
    // against all of them. Rasterize and RasterizeToRenderTargets dispose per-op through RasterizeAt;
    // RasterizeAndConcat renders into a single shared canvas and disposes through its catch sweep.
    private static IEnumerable<TestCaseData> RasterizeMethods()
    {
        yield return new TestCaseData((Action<RenderNodeProcessor>)(p => p.Rasterize()))
            .SetName("{m}(Rasterize)");
        yield return new TestCaseData((Action<RenderNodeProcessor>)(p => p.RasterizeAndConcat()))
            .SetName("{m}(RasterizeAndConcat)");
        yield return new TestCaseData((Action<RenderNodeProcessor>)(p => p.RasterizeToRenderTargets()))
            .SetName("{m}(RasterizeToRenderTargets)");
    }

    [TestCaseSource(nameof(RasterizeMethods))]
    public void DisposesFaultingAndRemainingOperations_WhenRenderThrows(Action<RenderNodeProcessor> rasterize)
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("first", disposed),
            CreateOperation("fault", disposed, throwOnRender: true),
            CreateOperation("remaining", disposed));
        var processor = new RenderNodeProcessor(node, useRenderCache: false);

        var ex = Assert.Throws<InvalidOperationException>(() => rasterize(processor));

        Assert.That(ex!.Message, Is.EqualTo("fault"));
        Assert.That(disposed, Is.EqualTo(new[] { "first", "fault", "remaining" }));
    }

    [TestCaseSource(nameof(RasterizeMethods))]
    public void DoesNotDoubleDisposeFaultingOperation_WhenDisposeThrows(Action<RenderNodeProcessor> rasterize)
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("first", disposed),
            CreateOperation("fault", disposed, throwOnDispose: true),
            CreateOperation("remaining", disposed));
        var processor = new RenderNodeProcessor(node, useRenderCache: false);

        // A double-dispose would re-run the faulting op's OnDispose (use-after-free for GPU-backed
        // ops) and skip the remaining op, so the faulting op must be disposed exactly once.
        var ex = Assert.Throws<InvalidOperationException>(() => rasterize(processor));

        Assert.That(ex!.Message, Is.EqualTo("fault"));
        Assert.That(disposed, Is.EqualTo(new[] { "first", "fault", "remaining" }));
    }

    [TestCaseSource(nameof(RasterizeMethods))]
    public void ContinuesCleanupAndPreservesOriginalException_WhenSweepDisposeThrows(
        Action<RenderNodeProcessor> rasterize)
    {
        var disposed = new List<string>();
        using var node = CreateRenderThrowWithThrowingRemainingOps(disposed);
        var processor = new RenderNodeProcessor(node, useRenderCache: false);

        var ex = Assert.Throws<InvalidOperationException>(() => rasterize(processor));

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

    // The faulting op throws on both render and dispose: the render throw must propagate while the
    // dispose throw is swallowed during cleanup (RasterizeAt's DisposeBestEffort, or the DisposeAll
    // sweep in RasterizeAndConcat's catch).
    [TestCaseSource(nameof(RasterizeMethods))]
    public void PreservesRenderException_WhenFaultingOpAlsoThrowsOnDispose(Action<RenderNodeProcessor> rasterize)
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("first", disposed),
            CreateOperation("render-fault", disposed, throwOnRender: true, throwOnDispose: true,
                disposeFaultMessage: "dispose-fault"),
            CreateOperation("remaining", disposed));
        var processor = new RenderNodeProcessor(node, useRenderCache: false);

        var ex = Assert.Throws<InvalidOperationException>(() => rasterize(processor));

        Assert.That(ex!.Message, Is.EqualTo("render-fault"));
        Assert.That(disposed, Is.EqualTo(new[] { "first", "render-fault", "remaining" }));
    }

    // A throwing RenderTarget.Dispose() during faulting-op cleanup must not mask the render exception.
    [TestCaseSource(nameof(RasterizeMethods))]
    public void PreservesRenderException_WhenRenderTargetDisposeThrows(Action<RenderNodeProcessor> rasterize)
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("render-fault", disposed, throwOnRender: true));
        var processor = new FakeRenderNodeProcessor(node, _ => true);

        var ex = Assert.Throws<InvalidOperationException>(() => rasterize(processor));

        Assert.That(ex!.Message, Is.EqualTo("render-fault"));
        Assert.That(processor.CreatedTargets, Has.Count.EqualTo(1));
        Assert.That(processor.CreatedTargets[0].DisposeWasCalled, Is.True);
        Assert.That(disposed, Is.EqualTo(new[] { "render-fault" }));
    }

    [TestCaseSource(nameof(RasterizeMethods))]
    public void DisposesPulledOperations_WhenRenderTargetCreateThrows(Action<RenderNodeProcessor> rasterize)
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("first", disposed),
            CreateOperation("second", disposed));
        var processor = new FakeRenderNodeProcessor(node, _ => false, throwOnCreate: true);

        var ex = Assert.Throws<InvalidOperationException>(() => rasterize(processor));

        Assert.That(ex!.Message, Is.EqualTo("rt-create-fault"));
        Assert.That(disposed, Is.EqualTo(new[] { "first", "second" }));
    }

    [TestCaseSource(nameof(RasterizeMethods))]
    public void DisposesPulledOperations_WhenRenderTargetCreateReturnsNull(Action<RenderNodeProcessor> rasterize)
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("first", disposed),
            CreateOperation("second", disposed));
        var processor = new FakeRenderNodeProcessor(node, _ => false, returnNullOnCreate: true);

        var ex = Assert.Throws<Exception>(() => rasterize(processor));

        Assert.That(ex!.Message, Is.EqualTo("RenderTarget is null"));
        Assert.That(disposed, Is.EqualTo(new[] { "first", "second" }));
    }

    // When the null-allocation path also hits a throwing op.Dispose(), the "RenderTarget is null"
    // failure must still surface rather than the op's dispose throw.
    [TestCaseSource(nameof(RasterizeMethods))]
    public void PreservesNullAllocationFailure_WhenOpDisposeAlsoThrows(Action<RenderNodeProcessor> rasterize)
    {
        var disposed = new List<string>();
        using var node = new StaticRenderNode(
            CreateOperation("first", disposed, throwOnDispose: true),
            CreateOperation("second", disposed));
        var processor = new FakeRenderNodeProcessor(node, _ => false, returnNullOnCreate: true);

        var ex = Assert.Throws<Exception>(() => rasterize(processor));

        Assert.That(ex!.Message, Is.EqualTo("RenderTarget is null"));
        Assert.That(disposed, Is.EqualTo(new[] { "first", "second" }));
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

        using var renderTarget = RenderTarget.CreateNull(4, 4);
        using var canvas = new ImmediateCanvas(renderTarget);

        var ex = Assert.Throws<InvalidOperationException>(() => processor.Render(canvas));

        Assert.That(ex!.Message, Is.EqualTo("fault"));
        // A mid-loop render throw must still dispose the faulting op and every op after it, or those
        // ops' GPU handles leak.
        Assert.That(disposed, Is.EqualTo(new[] { "first", "fault", "remaining" }));
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

        using var renderTarget = RenderTarget.CreateNull(4, 4);
        using var canvas = new ImmediateCanvas(renderTarget);

        var ex = Assert.Throws<InvalidOperationException>(() => processor.Render(canvas));

        Assert.That(ex!.Message, Is.EqualTo("fault"));
        Assert.That(disposed, Is.EqualTo(new[] { "first", "fault", "remaining" }));
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

        Assert.DoesNotThrow(() => RenderNodeOperation.DisposeAll(ops));
        Assert.That(disposed, Is.EqualTo(new[] { "first", "throws", "remaining" }));
    }

    // RasterizeToRenderTargets keeps successfully-rendered targets in a list, so a list-resident
    // target that throws on Dispose during cleanup must not stop the sweep or mask the render
    // exception. Rasterize snapshots and disposes each target immediately and RasterizeAndConcat
    // uses a single target, so neither has list-resident targets — this path is theirs alone.
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

    // Substitutes RenderTarget allocation so the exception-safety paths can run with a RenderTarget
    // whose Dispose() throws, or with a failing/null allocation, none of which a real GPU target offers.
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
