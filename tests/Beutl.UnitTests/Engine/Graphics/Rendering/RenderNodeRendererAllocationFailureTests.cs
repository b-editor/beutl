using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class RenderNodeRendererAllocationFailureTests
{
    private static readonly Rect s_domain = new(0, 0, 100, 100);

    [Test]
    public void PreviewMaterializationAllocationFailure_DropsContributionAndRecordsDiagnostics()
    {
        using FilterEffect.Resource resource = CreateStrokeEffectResource();
        using FilterEffectRenderNode node = CreateScene(resource);
        var factory = new FailSecondTargetFactory();
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var renderer = CreateRenderer(node, RenderIntent.Preview, factory, diagnostics);

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Bitmap bitmap = rasterization.Bitmap
            ?? throw new AssertionException("The preview allocation-drop request produced no bitmap.");
        PixelSize expectedDeviceSize = PixelRect.FromRect(s_domain, rasterization.OutputScale).Size;

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(rasterization.Bounds, Is.EqualTo(s_domain));
            Assert.That(rasterization.OutputScale, Is.EqualTo(1));
            Assert.That(new PixelSize(bitmap.Width, bitmap.Height), Is.EqualTo(expectedDeviceSize));
            Assert.That(bitmap.GetPixelSpan<ushort>().ToArray(), Is.All.Zero,
                "a dropped preview contribution must leave the cleared destination transparent");
            Assert.That(factory.FailureConsumed, Is.True);
            Assert.That(factory.CreateCalls, Is.EqualTo(2));
            Assert.That(factory.FailedDeviceSize, Is.EqualTo(new PixelSize(102, 102)));
            Assert.That(snapshot.Succeeded, Is.True);
            Assert.That(snapshot.FailurePhase, Is.Null);
            Assert.That(snapshot[RenderPipelineCounter.PreviewAllocationDrops], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.Failures], Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.CleanupFailures], Is.Zero);
        });
    }

    [Test]
    public void DeliveryMaterializationAllocationFailure_Throws()
    {
        using FilterEffect.Resource resource = CreateStrokeEffectResource();
        using FilterEffectRenderNode node = CreateScene(resource);
        var factory = new FailSecondTargetFactory();
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var renderer = CreateRenderer(node, RenderIntent.Delivery, factory, diagnostics);

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() =>
        {
            using RenderNodeRasterization unexpected = renderer.Rasterize();
        });
        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Message,
                Is.EqualTo("The render-target factory could not allocate 102x102 pixels."));
            Assert.That(factory.FailureConsumed, Is.True);
            Assert.That(factory.CreateCalls, Is.EqualTo(2));
            Assert.That(factory.FailedDeviceSize, Is.EqualTo(new PixelSize(102, 102)));
            Assert.That(snapshot.Succeeded, Is.False);
            Assert.That(snapshot.FailurePhase, Is.EqualTo(RenderPipelineFailurePhase.Allocation));
            Assert.That(snapshot[RenderPipelineCounter.PreviewAllocationDrops], Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.Failures], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.CleanupFailures], Is.Zero);
        });
    }

    [Test]
    public void PreviewGeometryCropAllocationFailure_DropsContributionAndRecordsDiagnostics()
    {
        using FilterEffect.Resource resource = new ShrinkingGeometryEffect()
            .ToResource(CompositionContext.Default);
        using FilterEffectRenderNode node = CreateScene(resource);
        var factory = new FailSpecificSizeTargetFactory(new PixelSize(98, 98));
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var renderer = CreateRenderer(node, RenderIntent.Preview, factory, diagnostics);

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;
        Bitmap bitmap = rasterization.Bitmap
            ?? throw new AssertionException("The preview Geometry crop allocation-drop request produced no bitmap.");

        Assert.Multiple(() =>
        {
            Assert.That(bitmap.GetPixelSpan<ushort>().ToArray(), Is.All.Zero,
                "a dropped Geometry crop must leave the cleared destination transparent");
            Assert.That(factory.FailureConsumed, Is.True);
            Assert.That(factory.FailedDeviceSize, Is.EqualTo(new PixelSize(98, 98)));
            Assert.That(snapshot.Succeeded, Is.True);
            Assert.That(snapshot.FailurePhase, Is.Null);
            Assert.That(snapshot[RenderPipelineCounter.PreviewAllocationDrops], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.Failures], Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.CleanupFailures], Is.Zero);
        });
    }

    [Test]
    public void DeliveryGeometryCropAllocationFailure_Throws()
    {
        using FilterEffect.Resource resource = new ShrinkingGeometryEffect()
            .ToResource(CompositionContext.Default);
        using FilterEffectRenderNode node = CreateScene(resource);
        var factory = new FailSpecificSizeTargetFactory(new PixelSize(98, 98));
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var renderer = CreateRenderer(node, RenderIntent.Delivery, factory, diagnostics);

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(() =>
        {
            using RenderNodeRasterization unexpected = renderer.Rasterize();
        });
        RenderPipelineDiagnosticSnapshot snapshot = diagnostics.Latest;

        Assert.Multiple(() =>
        {
            Assert.That(
                exception!.Message,
                Is.EqualTo("The render-target factory could not allocate 98x98 pixels."));
            Assert.That(factory.FailureConsumed, Is.True);
            Assert.That(factory.FailedDeviceSize, Is.EqualTo(new PixelSize(98, 98)));
            Assert.That(snapshot.Succeeded, Is.False);
            Assert.That(snapshot.FailurePhase, Is.EqualTo(RenderPipelineFailurePhase.Allocation));
            Assert.That(snapshot[RenderPipelineCounter.PreviewAllocationDrops], Is.Zero);
            Assert.That(snapshot[RenderPipelineCounter.Failures], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.CleanupFailures], Is.Zero);
        });
    }

    [Test]
    public void ZeroAreaOutput_DoesNotRecordPreviewAllocationDrop()
    {
        using FilterEffect.Resource resource = CreateStrokeEffectResource();
        using FilterEffectRenderNode node = CreateScene(resource);
        var factory = new CpuTargetFactory();
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var renderer = CreateRenderer(
            node,
            RenderIntent.Preview,
            factory,
            diagnostics,
            requestedRegion: new Rect(20, 30, 0, 10));

        using RenderNodeRasterization rasterization = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.True);
            Assert.That(diagnostics.Latest.Succeeded, Is.True);
            Assert.That(
                diagnostics.Latest[RenderPipelineCounter.PreviewAllocationDrops],
                Is.Zero);
        });
    }

    private static FilterEffect.Resource CreateStrokeEffectResource()
    {
        var pen = new Pen
        {
            Thickness = { CurrentValue = 9 },
            Brush = { CurrentValue = Brushes.OrangeRed },
        };
        var effect = new StrokeEffect
        {
            Pen = { CurrentValue = pen },
        };
        return effect.ToResource(CompositionContext.Default);
    }

    [SuppressResourceClassGeneration]
    private sealed partial class ShrinkingGeometryEffect : FilterEffect
    {
        private static readonly GeometryDescription s_geometry = GeometryDescription.Create(
            state: true,
            static (session, _) =>
            {
                session.Canvas.Use(session.Input.Draw);
                session.SetOutputBounds(session.OutputBounds.Inflate(new Thickness(-1)));
            },
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            structuralKey: "allocation-failure-shrinking-geometry");

        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
            => context.Geometry(s_geometry);

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource();
            bool updateOnly = false;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource
        {
            public Resource()
                : base(skipDefaultInitialization: true)
            {
            }
        }
    }

    private static FilterEffectRenderNode CreateScene(FilterEffect.Resource resource)
    {
        var node = new FilterEffectRenderNode(resource);
        node.AddChild(new EllipseRenderNode(s_domain, Brushes.Resource.White, null));
        return node;
    }

    private static RenderNodeRenderer CreateRenderer(
        RenderNode node,
        RenderIntent intent,
        IRenderTargetFactory factory,
        RenderPipelineDiagnosticsState diagnostics,
        Rect? requestedRegion = null)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = intent,
                    TargetDomain = s_domain,
                    RequestedRegion = requestedRegion,
                    OutputScale = 1,
                    MaxWorkingScale = intent == RenderIntent.Delivery
                    ? float.PositiveInfinity
                    : 2,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                    Purpose = RenderRequestPurpose.Frame,
                    Diagnostics = diagnostics,
                },
                TargetFactory = factory,
            });

    private sealed class FailSecondTargetFactory : CpuTargetFactory
    {
        public bool FailureConsumed { get; private set; }

        public PixelSize? FailedDeviceSize { get; private set; }

        public override RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            PixelSize deviceSize = allocation.DeviceSize;
            int index = CreateCalls++;
            if (index == 1)
            {
                FailureConsumed = true;
                FailedDeviceSize = deviceSize;
                return null;
            }

            return CreateTarget(deviceSize);
        }
    }

    private sealed class FailSpecificSizeTargetFactory(PixelSize failureSize) : CpuTargetFactory
    {
        public bool FailureConsumed { get; private set; }

        public PixelSize? FailedDeviceSize { get; private set; }

        public override RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            PixelSize deviceSize = allocation.DeviceSize;
            CreateCalls++;
            if (!FailureConsumed && deviceSize == failureSize)
            {
                FailureConsumed = true;
                FailedDeviceSize = deviceSize;
                return null;
            }

            return CreateTarget(deviceSize);
        }
    }

    private class CpuTargetFactory : IRenderTargetFactory
    {
        public int CreateCalls { get; protected set; }

        public virtual RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            PixelSize deviceSize = allocation.DeviceSize;
            CreateCalls++;
            return CreateTarget(deviceSize);
        }

        protected static RenderTarget CreateTarget(PixelSize deviceSize)
        {
            SKSurface surface = SKSurface.Create(new SKImageInfo(
                    deviceSize.Width,
                    deviceSize.Height,
                    SKColorType.RgbaF16,
                    SKAlphaType.Premul,
                    SKColorSpace.CreateSrgbLinear()))
                ?? throw new InvalidOperationException("Could not create the CPU allocation-failure test surface.");
            return new CpuRenderTarget(surface, deviceSize);
        }
    }

    private sealed class CpuRenderTarget(SKSurface surface, PixelSize size)
        : RenderTarget(surface, size.Width, size.Height);
}
