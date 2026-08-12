using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class BrushMaterializationSmokeTests
{
    [Test]
    public void DrawableBrush_IsMaterializedAtExecution()
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
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });

        RenderNodeMeasurement measurement = renderer.Measure();
        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap!;

        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasContributingValues, Is.True);
            Assert.That(measurement.OutputBounds, Is.EqualTo(new Rect(0, 0, 64, 36)));
            Assert.That(bitmap, Is.Not.Null);
            Assert.That(GetAlpha(bitmap, 32, 18), Is.GreaterThan(0.9f));
            Assert.That(GetAlpha(bitmap, 0, 0), Is.LessThan(0.1f));
        });
    }

    [Test]
    public void DrawableOpacityMask_IsMaterializedAtExecution()
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
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });

        RenderNodeMeasurement measurement = renderer.Measure();
        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap!;

        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasContributingValues, Is.True);
            Assert.That(measurement.OutputBounds, Is.EqualTo(new Rect(0, 0, 64, 36)));
            Assert.That(bitmap, Is.Not.Null);
            Assert.That(GetAlpha(bitmap, 32, 18), Is.GreaterThan(0.9f));
            Assert.That(GetAlpha(bitmap, 0, 0), Is.LessThan(0.1f));
        });
    }

    [Test]
    public void DrawableBrushInsidePresenter_IsMaterializedAtExecution()
    {
        var content = new EllipseShape
        {
            Width = { CurrentValue = 20 },
            Height = { CurrentValue = 12 },
            Fill = { CurrentValue = Brushes.White },
        };
        var drawableBrush = new DrawableBrush(content);
        var presenter = new BrushPresenter
        {
            Target = { CurrentValue = drawableBrush },
        };
        using BrushPresenter.Resource presenterResource = presenter.ToResource(CompositionContext.Default);
        using var node = new RectangleRenderNode(new Rect(0, 0, 64, 36), presenterResource, null);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });

        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Bitmap bitmap = rasterization.Bitmap!;

        Assert.Multiple(() =>
        {
            Assert.That(bitmap, Is.Not.Null);
            Assert.That(GetAlpha(bitmap, 32, 18), Is.GreaterThan(0.9f));
            Assert.That(GetAlpha(bitmap, 0, 0), Is.LessThan(0.1f));
        });
    }

    [Test]
    public void BrushPresenterCycle_IsRejectedDeterministically()
    {
        var first = new BrushPresenter();
        var second = new BrushPresenter();
        using BrushPresenter.Resource firstResource = first.ToResource(CompositionContext.Default);
        using BrushPresenter.Resource secondResource = second.ToResource(CompositionContext.Default);
        firstResource.Target = secondResource;
        secondResource.Target = firstResource;

        try
        {
            using var paint = new SKPaint();
            var constructor = new BrushConstructor(
                new Rect(0, 0, 64, 36),
                firstResource,
                BlendMode.SrcOver);

            Assert.That(
                () => constructor.ConfigurePaint(paint),
                Throws.InvalidOperationException.With.Message.Contains("BrushPresenter target cycle"));
        }
        finally
        {
            firstResource.Target = null;
            secondResource.Target = null;
        }
    }

    private static float GetAlpha(Bitmap bitmap, int x, int y)
        => (float)bitmap.GetRow<Half>(y)[(x * 4) + 3];
}
