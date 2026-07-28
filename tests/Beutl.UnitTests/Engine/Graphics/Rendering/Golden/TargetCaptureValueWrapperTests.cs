using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public sealed class TargetCaptureValueWrapperTests
{
    private static readonly Rect s_bounds = new(0, 0, 16, 12);

    [Test]
    public void MaterializedOpacity_TargetCaptureReadsCallerTargetAndCompositesCapturedValue()
    {
        using RenderNodeRasterization raster = Render(ValueWrapper.Opacity);

        Assert.Multiple(() =>
        {
            Assert.That(raster.Bounds, Is.EqualTo(s_bounds));
            Assert.That(AlphaAt(raster.Bitmap!, 8, 6), Is.EqualTo(0.627f).Within(0.03f));
        });
    }

    [Test]
    public void MaterializedOpacityMask_TargetCaptureReadsCallerTargetAndCompositesCapturedValue()
    {
        using RenderNodeRasterization raster = Render(ValueWrapper.OpacityMask);

        Assert.Multiple(() =>
        {
            Assert.That(raster.Bounds, Is.EqualTo(s_bounds));
            Assert.That(AlphaAt(raster.Bitmap!, 8, 6), Is.EqualTo(0.752f).Within(0.03f));
        });
    }

    [Test]
    public void TargetCapture_CropsTheDeclaredRegionWithoutResamplingTheWholeTarget()
    {
        var captureBounds = new Rect(8, 0, 8, 12);
        using Brush.Resource red = Brushes.Red.ToResource(CompositionContext.Default);
        using Brush.Resource blue = Brushes.Blue.ToResource(CompositionContext.Default);
        using var root = new ContainerRenderNode();
        root.AddChild(new RectangleRenderNode(new Rect(0, 0, 8, 12), red, null));
        root.AddChild(new RectangleRenderNode(captureBounds, blue, null));
        root.AddChild(new ContributingTargetCaptureNode(captureBounds));
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_bounds,
                TargetFactory = new CpuTargetFactory(),
                UseRenderCache = false,
                FusionMode = FusionMode.Disabled,
            });

        using RenderNodeRasterization raster = renderer.Rasterize();
        (float leftRed, float leftBlue) = RedBlueAt(raster.Bitmap!, 2, 6);
        (float capturedLeftRed, float capturedLeftBlue) = RedBlueAt(raster.Bitmap!, 9, 6);
        (float capturedRightRed, float capturedRightBlue) = RedBlueAt(raster.Bitmap!, 14, 6);

        Assert.Multiple(() =>
        {
            Assert.That(leftRed, Is.GreaterThan(leftBlue));
            Assert.That(capturedLeftBlue, Is.GreaterThan(capturedLeftRed),
                "the left edge of the capture must come from the declared right-half source region");
            Assert.That(capturedRightBlue, Is.GreaterThan(capturedRightRed));
        });
    }

    private static RenderNodeRasterization Render(ValueWrapper wrapper)
    {
        using Brush.Resource fill = new SolidColorBrush(new Color(128, 255, 0, 0))
            .ToResource(CompositionContext.Default);
        using var root = new ContainerRenderNode();
        root.AddChild(new RectangleRenderNode(s_bounds, fill, null));
        root.AddChild(new TargetCaptureValueWrapperNode(wrapper));
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_bounds,
                TargetFactory = new CpuTargetFactory(),
                UseRenderCache = false,
                FusionMode = FusionMode.Disabled,
            });

        return renderer.Rasterize();
    }

    private static float AlphaAt(Bitmap bitmap, int x, int y)
    {
        Span<ushort> row = bitmap.GetRow<ushort>(y);
        return (float)BitConverter.UInt16BitsToHalf(row[(x * 4) + 3]);
    }

    private static (float Red, float Blue) RedBlueAt(Bitmap bitmap, int x, int y)
    {
        Span<ushort> row = bitmap.GetRow<ushort>(y);
        int offset = x * 4;
        return (
            (float)BitConverter.UInt16BitsToHalf(row[offset]),
            (float)BitConverter.UInt16BitsToHalf(row[offset + 2]));
    }

    private enum ValueWrapper
    {
        Opacity,
        OpacityMask,
    }

    private sealed class TargetCaptureValueWrapperNode(ValueWrapper wrapper) : RenderNode
    {
        private static readonly GeometryDescription s_identityGeometry = GeometryDescription.Create(
            static session => session.Canvas.Use(session.Input.Draw),
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            structuralKey: typeof(TargetCaptureValueWrapperNode),
            runtimeIdentity: new RenderRuntimeIdentity("target-capture-value-wrapper-identity"));

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle capture = context.TargetCapture(TargetCaptureDescription.Create(
                TargetRegion.Region(s_bounds),
                s_bounds,
                RenderHitTestContract.OutputBounds,
                RenderScaleContract.MaterializeAtWorkingScale));
            RenderFragmentHandle wrapped = wrapper switch
            {
                ValueWrapper.Opacity => context.Opacity(capture, 0.5f),
                ValueWrapper.OpacityMask => context.OpacityMask(
                    capture,
                    Brushes.Resource.White,
                    s_bounds),
                _ => throw new InvalidOperationException("The value-wrapper fixture is invalid."),
            };
            RenderFragmentHandle materialized = context.Geometry(wrapped, s_identityGeometry);
            context.Publish(context.ContributeValues(materialized));
        }
    }

    private sealed class ContributingTargetCaptureNode(Rect bounds) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle capture = context.TargetCapture(TargetCaptureDescription.Create(
                TargetRegion.Region(bounds),
                bounds,
                RenderHitTestContract.OutputBounds,
                RenderScaleContract.MaterializeAtWorkingScale));
            context.Publish(context.ContributeValues(capture));
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(PixelSize deviceSize)
            => new CpuRenderTarget(deviceSize.Width, deviceSize.Height);
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);
}
