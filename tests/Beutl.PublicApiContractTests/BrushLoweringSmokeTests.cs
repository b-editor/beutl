using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class BrushLoweringSmokeTests
{
    [Test]
    public void DrawableBrush_IsRecordedAsAValueDependency()
    {
        var content = new EllipseShape();
        content.Width.CurrentValue = 20;
        content.Height.CurrentValue = 12;
        content.Fill.CurrentValue = Brushes.White;
        var brush = new DrawableBrush(content);
        using DrawableBrush.Resource brushResource = brush.ToResource(CompositionContext.Default);
        using var node = new RectangleRenderNode(new Rect(0, 0, 64, 36), brushResource, null);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions { UseRenderCache = false });

        RenderNodeMeasurement measurement = renderer.Measure();
        using RenderNodeRasterization rasterization = renderer.Rasterize();
        bool containsPixels = rasterization.Bitmap?.GetPixelSpan().ContainsAnyExcept((byte)0) == true;

        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasContributingValues, Is.True);
            Assert.That(measurement.OutputBounds, Is.EqualTo(new Rect(0, 0, 64, 36)));
            Assert.That(rasterization.Bitmap, Is.Not.Null);
            Assert.That(containsPixels, Is.True);
        });
    }

    [Test]
    public void DrawableOpacityMask_RecordsPrimaryAndBrushDependency()
    {
        var content = new EllipseShape();
        content.Width.CurrentValue = 20;
        content.Height.CurrentValue = 12;
        content.Fill.CurrentValue = Brushes.White;
        var brush = new DrawableBrush(content);
        using DrawableBrush.Resource brushResource = brush.ToResource(CompositionContext.Default);
        using var root = new OpacityMaskRenderNode(brushResource, new Rect(0, 0, 64, 36), false);
        root.AddChild(new RectangleRenderNode(new Rect(0, 0, 64, 36), Brushes.Resource.White, null));
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions { UseRenderCache = false });

        RenderNodeMeasurement measurement = renderer.Measure();
        using RenderNodeRasterization rasterization = renderer.Rasterize();
        bool containsPixels = rasterization.Bitmap?.GetPixelSpan().ContainsAnyExcept((byte)0) == true;

        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasContributingValues, Is.True);
            Assert.That(measurement.OutputBounds, Is.EqualTo(new Rect(0, 0, 64, 36)));
            Assert.That(rasterization.Bitmap, Is.Not.Null);
            Assert.That(containsPixels, Is.True);
        });
    }
}
