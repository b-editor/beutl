using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

public sealed class AllocationPreflightTests
{
    private static readonly Rect s_bounds = new(0, 0, 8, 8);

    [Test]
    public void StaticPeakFailsBeforeTheFactoryAcquiresTheRoot()
    {
        using var root = new StaticLayerFanoutNode(s_bounds);
        var factory = new TrackingTargetFactory();
        using var renderer = CreateRenderer(
            root,
            factory,
            new RenderAllocationBudget(
                maximumLiveBytes: 1024 * 1024,
                maximumLiveTargets: 3));

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("planned render-target lifetime schedule"));
            Assert.That(factory.CreateCalls, Is.Zero,
                "Static lifetime validation must run before the root lease reaches the factory.");
        });
    }

    [Test]
    public void StaticFactoryDimensionFailsBeforeTheFactoryAcquiresTheRoot()
    {
        var layerDomain = new Rect(0, 0, 9, 1);
        using var root = new StaticLayerNode(new Rect(0, 0, 1, 1), layerDomain);
        var factory = new TrackingTargetFactory(maximumDimension: 8);
        using var renderer = CreateRenderer(
            root,
            factory,
            RenderAllocationBudget.Default,
            requestedRegion: new Rect(0, 0, 1, 1),
            targetDomain: layerDomain);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("8-pixel dimension limit"));
            Assert.That(factory.CreateCalls, Is.Zero);
        });
    }

    [Test]
    public void TargetlessSameSizeAllocation_RequeriesLimitAfterContextBinding()
    {
        var factory = new ContextSensitiveTargetFactory();
        using var registry = new RenderTargetLeaseRegistry(factory);
        using RenderTargetLeaseSession session = registry.BeginSession(
            RenderIntent.Delivery,
            RenderAllocationBudget.Default);

        using (RenderTargetLease first = session.Acquire(new PixelSize(8, 8)))
        {
        }

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => session.Acquire(new PixelSize(8, 8)));

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("4-pixel dimension limit"));
            Assert.That(factory.CreateCalls, Is.EqualTo(1));
            Assert.That(factory.DimensionQueries.Select(item => item.GraphicsContextHandle),
                Is.EqualTo(new nint?[] { null, 0 }));
        });
    }

    [Test]
    public void ZeroOffsetStaticAllocationClampsToTheEngineDimensionLimit()
    {
        var layerDomain = new Rect(0, 0, RenderScaleUtilities.MaxBufferDimension + 1, 1);
        using var root = new StaticLayerNode(new Rect(0, 0, 1, 1), layerDomain);
        var factory = new TrackingTargetFactory();
        using var renderer = CreateRenderer(
            root,
            factory,
            RenderAllocationBudget.Default,
            requestedRegion: new Rect(0, 0, 1, 1),
            targetDomain: layerDomain);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(factory.RequestedSizes, Is.Not.Empty);
            Assert.That(
                factory.RequestedSizes.All(size => size.Width <= RenderScaleUtilities.MaxBufferDimension
                                                   && size.Height <= RenderScaleUtilities.MaxBufferDimension),
                Is.True,
                "A zero device-grid offset must not bypass the normal per-buffer dimension clamp.");
        });
    }

    [Test]
    public void RuntimeSizedOpaqueOutputRechecksTheSharedLedger()
    {
        using var root = new DynamicOpaqueSourceNode(s_bounds);
        var factory = new TrackingTargetFactory();
        using var renderer = CreateRenderer(
            root,
            factory,
            new RenderAllocationBudget(
                maximumLiveBytes: 1024 * 1024,
                maximumLiveTargets: 1));

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("allocation budget"));
            Assert.That(failure.Message, Does.Not.Contain("planned render-target lifetime schedule"));
            Assert.That(factory.CreateCalls, Is.EqualTo(1),
                "The statically known root fits; the runtime-sized callback output must fail on its actual acquire.");
        });
    }

    [Test]
    public void PreviewExactFitPreservesTheDirectShaderInput()
    {
        using var root = new OptionalLayerShaderNode(s_bounds);
        var factory = new TrackingTargetFactory();
        using var renderer = CreateRenderer(
            root,
            factory,
            new RenderAllocationBudget(
                maximumLiveBytes: 2 * 8 * 8 * 8,
                maximumLiveTargets: 2),
            intent: RenderIntent.Preview);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(rasterization.Bitmap!.SKBitmap.GetPixel(4, 4).Red, Is.GreaterThan(200),
                "A direct root Shader must not make preflight count an output target that execution never acquires.");
            Assert.That(factory.CreateCalls, Is.EqualTo(2),
                "The exact-fit schedule consists of the root target and the Shader input Layer.");
        });
    }

    [Test]
    public void PreviewStaticEligiblePeakPlansTheDropBeforeExecution()
    {
        using var root = new OptionalLayerShaderNode(s_bounds);
        var factory = new TrackingTargetFactory();
        using var renderer = CreateRenderer(
            root,
            factory,
            new RenderAllocationBudget(
                maximumLiveBytes: 8 * 8 * 8,
                maximumLiveTargets: 1),
            intent: RenderIntent.Preview);

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False,
                "The preview root remains a valid transparent output when its effect contribution is dropped.");
            Assert.That(rasterization.Bitmap!.GetPixelSpan().ToArray(), Has.All.Zero,
                "The compiler-approved Layer drop must remove the optional effect contribution.");
            Assert.That(factory.CreateCalls, Is.EqualTo(1),
                "Only the mandatory root target may reach the factory after the optional materialization is planned out.");
        });
    }

    [Test]
    public void PreviewMandatoryOnlyPeakStillFailsBeforeTheFactory()
    {
        using var root = new StaticLayerFanoutNode(s_bounds);
        var factory = new TrackingTargetFactory();
        using var renderer = CreateRenderer(
            root,
            factory,
            new RenderAllocationBudget(
                maximumLiveBytes: 1024 * 1024,
                maximumLiveTargets: 3),
            intent: RenderIntent.Preview);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("planned render-target lifetime schedule"));
            Assert.That(factory.CreateCalls, Is.Zero);
        });
    }

    [Test]
    public void DeliveryTreatsAnEligibleStaticLifetimeAsMandatory()
    {
        using var root = new OptionalLayerShaderNode(s_bounds);
        var factory = new TrackingTargetFactory();
        using var renderer = CreateRenderer(
            root,
            factory,
            new RenderAllocationBudget(
                maximumLiveBytes: 8 * 8 * 8,
                maximumLiveTargets: 1));

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("planned render-target lifetime schedule"));
            Assert.That(factory.CreateCalls, Is.Zero);
        });
    }

    [Test]
    public void NonPixelAlignedDirectShaderFallbackIsIncludedInDeliveryPreflight()
    {
        using var root = new OptionalLayerShaderNode(s_bounds);
        var factory = new TrackingTargetFactory();
        using var renderer = CreateRenderer(
            root,
            factory,
            new RenderAllocationBudget(
                maximumLiveBytes: 8 * 8 * 8,
                maximumLiveTargets: 1));
        using RenderTarget target = RenderTarget.Create(8, 8)!;
        using var canvas = new ImmediateCanvas(
            target,
            density: 1,
            logicalSize: s_bounds.Size,
            intent: RenderIntent.Delivery);

        InvalidOperationException? failure;
        using (canvas.PushTransform(Matrix.CreateTranslation(0.5f, 0)))
        {
            failure = Assert.Throws<InvalidOperationException>(() => renderer.Render(canvas));
        }

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("planned render-target lifetime schedule"));
            Assert.That(factory.CreateCalls, Is.Zero,
                "A Shader that cannot draw on exact device pixels must have its fallback output planned before allocation.");
        });
    }

    private static RenderNodeRenderer CreateRenderer(
        RenderNode root,
        IRenderTargetFactory factory,
        RenderAllocationBudget budget,
        Rect? requestedRegion = null,
        Rect? targetDomain = null,
        RenderIntent intent = RenderIntent.Delivery)
        => new(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = intent,
                    TargetDomain = targetDomain ?? s_bounds,
                    RequestedRegion = requestedRegion,
                    CacheOptions = RenderCacheOptions.Disabled,
                    AllocationBudget = budget,
                },
                TargetFactory = factory,
            });

    private static RenderFragmentHandle RecordDynamicSource(RenderNodeContext context, Rect bounds)
    {
        OpaqueRenderDescription description = OpaqueRenderDescription.Create(
            bounds,
            static (session, currentBounds) =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(currentBounds);
                output.Canvas.Use(static canvas => canvas.Clear());
                session.Publish(output);
            },
            bounds: OpaqueRenderBoundsContract.Source(bounds),
            hitTest: RenderHitTestContract.None,
            valueCardinality: RenderValueCardinality.Single,
            scale: RenderScaleContract.Custom(static _ => 1));
        return context.OpaqueSource(description);
    }

    private sealed class DynamicOpaqueSourceNode(Rect bounds) : RenderNode
    {
        public override void Process(RenderNodeContext context)
            => context.Publish(RecordDynamicSource(context, bounds));
    }

    private sealed class StaticLayerNode(Rect sourceBounds, Rect layerDomain) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = RecordDynamicSource(context, sourceBounds);
            context.Publish(context.Layer([source], layerDomain));
        }
    }

    private sealed class StaticLayerFanoutNode(Rect bounds) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle first = context.Layer(
                [RecordDynamicSource(context, bounds)],
                bounds);
            RenderFragmentHandle second = context.Layer(
                [RecordDynamicSource(context, bounds)],
                bounds);
            context.Publish(context.Layer([first, second], bounds));
        }
    }

    private sealed class OptionalLayerShaderNode(Rect bounds) : RenderNode
    {
        private static readonly ShaderDescription s_identity = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return color; }");

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.PaintedSource(
                state: bounds,
                draw: static (session, rect) =>
                    session.Canvas.DrawRectangle(rect, session.Fill, session.Pen),
                fill: (Brushes.Resource.Red, Brushes.Resource.Red.Version),
                pen: null,
                brushBounds: bounds,
                outputBounds: bounds,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector,
                structuralKey: typeof(OptionalLayerShaderNode));
            RenderFragmentHandle layer = context.Layer([source], bounds);
            context.Publish(context.Shader(layer, s_identity));
        }
    }

    private sealed class TrackingTargetFactory(
        int maximumDimension = RenderScaleUtilities.MaxBufferDimension) : IRenderTargetFactory
    {
        public int CreateCalls { get; private set; }

        public List<PixelSize> RequestedSizes { get; } = [];

        public int GetMaximumDimension(RenderTargetAllocationDescriptor allocation) => maximumDimension;

        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            CreateCalls++;
            RequestedSizes.Add(allocation.DeviceSize);
            return RenderTarget.Create(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
        }
    }

    private sealed class ContextSensitiveTargetFactory : IRenderTargetFactory
    {
        public int CreateCalls { get; private set; }

        public List<RenderTargetAllocationDescriptor> DimensionQueries { get; } = [];

        public int GetMaximumDimension(RenderTargetAllocationDescriptor allocation)
        {
            DimensionQueries.Add(allocation);
            return allocation.GraphicsContextHandle is null ? 8 : 4;
        }

        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            CreateCalls++;
            var info = new SKImageInfo(
                allocation.DeviceSize.Width,
                allocation.DeviceSize.Height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear());
            SKSurface? surface = SKSurface.Create(info);
            return surface is null ? null : new CpuRenderTarget(surface, allocation.DeviceSize);
        }
    }

    private sealed class CpuRenderTarget(SKSurface surface, PixelSize size)
        : RenderTarget(surface, size.Width, size.Height);
}
