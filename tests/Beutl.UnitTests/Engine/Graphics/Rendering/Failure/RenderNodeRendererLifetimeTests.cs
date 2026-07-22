using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Failure;

[TestFixture]
public sealed class RenderNodeRendererLifetimeTests
{
    [Test]
    public void Rasterize_EmptySelection_ReturnsEmptyResultWithoutAllocating()
    {
        var bounds = new Rect(0, 0, 8, 8);
        var emptySelection = new Rect(3, 4, 0, 2);
        using var source = new TrackingRenderTarget(new PixelSize(8, 8));
        using var node = new ShaderNode(source, bounds);
        using var factory = new TrackingTargetFactory();
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = bounds,
                RequestedRegion = emptySelection,
                OutputScale = 2,
                MaxWorkingScale = 2,
                UseRenderCache = false,
                TargetFactory = factory,
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.Bounds, Is.EqualTo(emptySelection));
            Assert.That(rasterization.OutputScale, Is.EqualTo(2));
            Assert.That(rasterization.IsEmpty, Is.True);
            Assert.That(rasterization.Bitmap, Is.Null);
            Assert.That(factory.Targets, Is.Empty);
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void Rasterize_EmptySelection_SurfacesLeaseSessionCleanupFailure()
    {
        var bounds = new Rect(0, 0, 8, 8);
        var cleanup = new InvalidOperationException("empty-selection-target-cleanup");
        using var source = new TrackingRenderTarget(new PixelSize(8, 8));
        using var node = new ShaderNode(source, bounds);
        using var factory = new TrackingTargetFactory(
            index => index == 0 ? cleanup : null);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = bounds,
                OutputScale = 1,
                MaxWorkingScale = 1,
                UseRenderCache = false,
                TargetFactory = factory,
            });

        using (RenderNodeRasterization seeded = renderer.Rasterize())
        {
            Assert.That(seeded.Bitmap, Is.Not.Null);
        }

        TrackingRenderTarget faultingTarget = factory.Targets[0];
        node.PublishOutput = false;
        Exception? observed = null;
        int emptyResults = 0;
        const int safetyLimit = 512;
        while (!faultingTarget.IsDisposed && emptyResults < safetyLimit)
        {
            try
            {
                using RenderNodeRasterization empty = renderer.Rasterize();
                Assert.That(empty.Bitmap, Is.Null);
                emptyResults++;
            }
            catch (Exception ex)
            {
                observed = ex;
                break;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(observed, Is.SameAs(cleanup));
            Assert.That(emptyResults, Is.GreaterThan(0));
            Assert.That(faultingTarget.IsDisposed, Is.True);
            Assert.That(faultingTarget.DisposeCalls, Is.EqualTo(1));
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
        });
    }

    [Test]
    public void Dispose_ReleasesRendererOwnedState_AndPreservesBorrowedState()
    {
        var bounds = new Rect(0, 0, 8, 8);
        using var source = new TrackingRenderTarget(new PixelSize(8, 8));
        source.Value.Canvas.Clear(new SKColor(80, 120, 160, 192));
        using var node = new ShaderNode(source, bounds);
        using var cacheSeed = new TrackingRenderTarget(new PixelSize(8, 8));
        node.Cache.StoreCache(cacheSeed, bounds);
        using var factory = new TrackingTargetFactory();
        var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = bounds,
                OutputScale = 1,
                MaxWorkingScale = 1,
                UseRenderCache = false,
                TargetFactory = factory,
            });

        RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap
            ?? throw new AssertionException("The lifetime test requires a non-empty rasterization.");

        Assert.Multiple(() =>
        {
            Assert.That(renderer.StructuralPlanCacheStatistics.RetainedPlans, Is.EqualTo(1));
            Assert.That(renderer.ProgramCacheStatistics.RetainedPrograms, Is.EqualTo(1));
            Assert.That(renderer.TargetPoolStatistics.OwnedTargets, Is.GreaterThanOrEqualTo(1));
            Assert.That(factory.Targets, Is.Not.Empty);
            Assert.That(factory.Targets, Has.All.Matches<TrackingRenderTarget>(target => !target.IsDisposed));
        });

        renderer.Dispose();
        using RenderTarget cached = node.Cache.UseCache(out Rect cachedBounds);

        Assert.Multiple(() =>
        {
            Assert.That(renderer.StructuralPlanCacheStatistics.RetainedPlans, Is.Zero);
            Assert.That(renderer.ProgramCacheStatistics.RetainedPrograms, Is.Zero);
            Assert.That(renderer.ProgramCacheStatistics.RetainedBytes, Is.Zero);
            Assert.That(renderer.TargetPoolStatistics.OwnedTargets, Is.Zero);
            Assert.That(renderer.TargetPoolStatistics.OwnedBytes, Is.Zero);
            Assert.That(factory.Targets, Has.All.Matches<TrackingRenderTarget>(target => target.IsDisposed));
            Assert.That(factory.Targets, Has.All.Matches<TrackingRenderTarget>(target => target.DisposeCalls == 1));
            Assert.That(factory.IsDisposed, Is.False, "The caller owns the target factory.");
            Assert.That(node.IsDisposed, Is.False, "The caller owns the root node.");
            Assert.That(node.Cache.IsDisposed, Is.False, "The root owns its render cache.");
            Assert.That(cachedBounds, Is.EqualTo(bounds));
            Assert.That(cached.IsDisposed, Is.False);
            Assert.That(source.IsDisposed, Is.False, "Borrowed materialized inputs remain caller-owned.");
            Assert.That(bitmap.IsDisposed, Is.False, "A returned rasterization owns its bitmap independently.");
        });

        rasterization.Dispose();
        Assert.That(bitmap.IsDisposed, Is.True);
    }

    private sealed class ShaderNode(RenderTarget source, Rect bounds) : RenderNode
    {
        public bool PublishOutput { get; set; } = true;

        public override void Process(RenderNodeContext context)
        {
            if (!PublishOutput)
                return;

            RenderResource<RenderTarget> target = context.Borrow(
                source,
                "renderer-lifetime-source",
                version: 1);
            RenderFragmentHandle input = context.MaterializedInput(
                MaterializedInputDescription.FromRenderTarget(
                    target,
                    bounds,
                    EffectiveScale.At(1),
                    RenderHitTestContract.OutputBounds));
            ShaderDescription shader = ShaderDescription.CurrentPixel(
                "uniform float gain; half4 apply(half4 color) { return color * gain; }",
                static bindings => bindings.Uniform("gain", 0.75f));
            context.Publish(context.Shader(input, shader));
        }
    }

    private sealed class TrackingTargetFactory(
        Func<int, Exception?>? disposeFailureAt = null) : IRenderTargetFactory, IDisposable
    {
        public List<TrackingRenderTarget> Targets { get; } = [];

        public bool IsDisposed { get; private set; }

        public RenderTarget Create(PixelSize deviceSize)
        {
            var target = new TrackingRenderTarget(
                deviceSize,
                disposeFailureAt?.Invoke(Targets.Count));
            Targets.Add(target);
            return target;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    private sealed class TrackingRenderTarget : RenderTarget
    {
        private static readonly SKColorSpace s_colorSpace = SKColorSpace.CreateSrgbLinear();
        private readonly Exception? _disposeFailure;

        public TrackingRenderTarget(PixelSize size, Exception? disposeFailure = null)
            : base(CreateSurface(size), size.Width, size.Height)
        {
            _disposeFailure = disposeFailure;
        }

        public int DisposeCalls { get; private set; }

        protected override void Dispose(bool disposing)
        {
            bool shouldThrow = disposing && !IsDisposed && _disposeFailure is not null;
            if (disposing && !IsDisposed)
                DisposeCalls++;

            base.Dispose(disposing);
            if (shouldThrow)
                throw _disposeFailure!;
        }

        private static SKSurface CreateSurface(PixelSize size)
            => SKSurface.Create(new SKImageInfo(
                   size.Width,
                   size.Height,
                   SKColorType.RgbaF16,
                   SKAlphaType.Premul,
                   s_colorSpace))
               ?? throw new InvalidOperationException("Could not create the lifetime-test render target.");
    }
}
