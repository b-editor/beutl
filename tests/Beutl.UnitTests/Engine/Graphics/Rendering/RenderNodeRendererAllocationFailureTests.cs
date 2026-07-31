using Beutl.Composition;
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

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(factory.FailureConsumed, Is.True);
            Assert.That(factory.CreateCalls, Is.EqualTo(2));
            Assert.That(snapshot.Succeeded, Is.True);
            Assert.That(snapshot.FailurePhase, Is.Null);
            Assert.That(snapshot[RenderPipelineCounter.PreviewAllocationDrops], Is.EqualTo(1));
            Assert.That(snapshot[RenderPipelineCounter.Failures], Is.Zero);
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

        InvalidOperationException? exception = Assert.Throws<InvalidOperationException>(
            () => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("could not allocate"));
            Assert.That(factory.FailureConsumed, Is.True);
            Assert.That(diagnostics.Latest.Succeeded, Is.False);
            Assert.That(
                diagnostics.Latest[RenderPipelineCounter.PreviewAllocationDrops],
                Is.Zero);
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
                Intent = intent,
                TargetDomain = s_domain,
                RequestedRegion = requestedRegion,
                OutputScale = 1,
                MaxWorkingScale = intent == RenderIntent.Delivery
                    ? float.PositiveInfinity
                    : 2,
                UseRenderCache = false,
                TargetFactory = factory,
                RenderPurpose = RenderRequestPurpose.Frame,
                Diagnostics = diagnostics,
            });

    private sealed class FailSecondTargetFactory : CpuTargetFactory
    {
        public bool FailureConsumed { get; private set; }

        public override RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            PixelSize deviceSize = allocation.DeviceSize;
            int index = CreateCalls++;
            if (index == 1)
            {
                FailureConsumed = true;
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
