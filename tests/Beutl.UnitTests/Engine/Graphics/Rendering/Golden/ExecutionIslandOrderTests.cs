using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

/// <summary>
/// Pixel coverage for the order execution islands run in. The ledger no longer checks completion order at
/// runtime, so a traversal that entered islands out of dependency or painter order would raise nothing: only
/// the composited pixels report it.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class ExecutionIslandOrderTests
{
    private static readonly Rect s_frame = new(0, 0, 16, 12);

    [Test]
    public void OverlappingSiblingIslands_LeaveTheLaterPublicationOwningTheOverlap()
    {
        using Brush.Resource red = Brushes.Red.ToResource(CompositionContext.Default);
        using Brush.Resource blue = Brushes.Blue.ToResource(CompositionContext.Default);
        using var root = new ContainerRenderNode();
        root.AddChild(new RectangleRenderNode(new Rect(0, 0, 12, 12), red, null));
        root.AddChild(new RectangleRenderNode(new Rect(4, 0, 12, 12), blue, null));

        using RenderNodeRasterization raster = Rasterize(root);
        (float leftRed, float leftBlue) = RedBlueAt(raster.Bitmap!, 2, 6);
        (float overlapRed, float overlapBlue) = RedBlueAt(raster.Bitmap!, 8, 6);
        (float rightRed, float rightBlue) = RedBlueAt(raster.Bitmap!, 14, 6);

        Assert.Multiple(() =>
        {
            Assert.That(leftRed, Is.GreaterThan(leftBlue), "the first island alone covers the left columns");
            Assert.That(
                overlapBlue,
                Is.GreaterThan(overlapRed),
                "the overlap must carry the island that published second, not the one that published first");
            Assert.That(rightBlue, Is.GreaterThan(rightRed), "the second island alone covers the right columns");
        });
    }

    [TestCase(8, 6)]
    [TestCase(1, 1)]
    [TestCase(15, 11)]
    public void TargetCaptureIsland_SamplesWhatTheEarlierSiblingIslandWrote(int x, int y)
    {
        using Brush.Resource fill = new SolidColorBrush(new Color(128, 255, 0, 0))
            .ToResource(CompositionContext.Default);
        using var root = new ContainerRenderNode();
        root.AddChild(new RectangleRenderNode(s_frame, fill, null));
        root.AddChild(new ContributingTargetCaptureNode(s_frame));

        using RenderNodeRasterization raster = Rasterize(root);

        // 0.502 + 0.502 * (1 - 0.502): the captured coverage composited over the coverage it captured.
        // A capture that ran first would sample an untouched target and leave 0.502.
        Assert.That(AlphaAt(raster.Bitmap!, x, y), Is.EqualTo(0.752f).Within(0.03f));
    }

    [Test]
    public void BlendIsland_ErasesFromTheDestinationTheEarlierIslandAlreadyWrote()
    {
        using Brush.Resource red = Brushes.Red.ToResource(CompositionContext.Default);
        using Brush.Resource white = Brushes.White.ToResource(CompositionContext.Default);
        using var root = new ContainerRenderNode();
        root.AddChild(new RectangleRenderNode(s_frame, red, null));
        root.AddChild(new BlendedRectangleNode(new Rect(8, 0, 8, 12), white, BlendMode.DstOut));

        using RenderNodeRasterization raster = Rasterize(root);

        Assert.Multiple(() =>
        {
            Assert.That(AlphaAt(raster.Bitmap!, 2, 6), Is.EqualTo(1f).Within(0.03f), "the unblended half stays opaque");
            Assert.That(
                AlphaAt(raster.Bitmap!, 12, 6),
                Is.EqualTo(0f).Within(0.03f),
                "the blend must erase the destination the earlier island wrote, not an empty target");
        });
    }

    [Test]
    public void StructuralPlanCache_KeepsReversePublicationOrderOnColdAndWarmPlans()
    {
        using Brush.Resource red = Brushes.Red.ToResource(CompositionContext.Default);
        using Brush.Resource blue = Brushes.Blue.ToResource(CompositionContext.Default);
        using var root = new ReversePublicationContainerRenderNode();
        root.AddChild(new RectangleRenderNode(new Rect(0, 0, 12, 12), red, null));
        root.AddChild(new RectangleRenderNode(new Rect(4, 0, 12, 12), blue, null));
        using RenderNodeRenderer renderer = CreateRenderer(root, RenderCacheOptions.Disabled);

        using RenderNodeRasterization cold = renderer.Rasterize();
        using RenderNodeRasterization warm = renderer.Rasterize();

        Assert.Multiple(() =>
        {
            AssertReversePublicationOrder(cold.Bitmap!, "the cold plan");
            AssertReversePublicationOrder(warm.Bitmap!, "the reused plan");
            Assert.That(renderer.StructuralPlanCacheStatistics.Compilations, Is.EqualTo(1));
            Assert.That(renderer.StructuralPlanCacheStatistics.Hits, Is.EqualTo(1),
                "the warm frame must reuse the graph-independent plan");
        });
    }

    private static RenderNodeRasterization Rasterize(RenderNode root)
    {
        using RenderNodeRenderer renderer = CreateRenderer(
            root,
            RenderCacheOptions.Disabled);

        return renderer.Rasterize();
    }

    private static RenderNodeRenderer CreateRenderer(
        RenderNode root,
        RenderCacheOptions cacheOptions)
        => new(root, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = s_frame,
            CacheOptions = cacheOptions,
        }, new CpuTargetFactory());

    private static void AssertReversePublicationOrder(Bitmap bitmap, string frame)
    {
        (float leftRed, float leftBlue) = RedBlueAt(bitmap, 2, 6);
        (float overlapRed, float overlapBlue) = RedBlueAt(bitmap, 8, 6);
        (float rightRed, float rightBlue) = RedBlueAt(bitmap, 14, 6);

        Assert.Multiple(() =>
        {
            Assert.That(leftRed, Is.GreaterThan(leftBlue), $"{frame}: only red covers the left columns");
            Assert.That(overlapRed, Is.EqualTo(1f).Within(0.03f),
                $"{frame}: red was authored first but published last, so it must own the overlap");
            Assert.That(overlapBlue, Is.EqualTo(0f).Within(0.03f),
                $"{frame}: the earlier blue publication must be covered in the overlap");
            Assert.That(rightBlue, Is.GreaterThan(rightRed), $"{frame}: only blue covers the right columns");
        });
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

    private sealed class ContributingTargetCaptureNode(Rect bounds) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle capture = context.TargetCapture(TargetCaptureDescription.Create(
                TargetRegion.Region(bounds),
                bounds,
                RenderHitTestContract.OutputBounds,
                TargetCaptureScaleContract.MaterializeAtWorkingScale));
            context.Publish(context.ContributeValues(capture));
        }
    }

    private sealed class BlendedRectangleNode(Rect rect, Brush.Resource fill, BlendMode blendMode) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.PaintedSource(
                rect,
                draw: static (canvas, fill, pen, rect) => canvas.DrawRectangle(rect, fill, pen),
                fill: fill,
                pen: null,
                outputBounds: rect,
                hitTest: RenderHitTestContract.OutputBounds,
                scale: RenderScaleContract.Vector);
            context.Publish(context.Blend(source, blendMode));
        }
    }

    private sealed class ReversePublicationContainerRenderNode : ContainerRenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            for (int index = context.Inputs.Count - 1; index >= 0; index--)
                context.Publish(context.Inputs[index]);
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
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
