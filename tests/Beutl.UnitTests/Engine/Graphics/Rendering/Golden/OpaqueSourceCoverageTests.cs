using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public sealed class OpaqueSourceCoverageTests
{
    private static readonly PixelSize s_frame = new(200, 200);

    [TestCase(false)]
    [TestCase(true)]
    public void MaterializingAnOpaqueSource_KeepsItsAntialiasedCoverage(bool shifted)
    {
        using Drawable.Resource direct = CreateAliasingProneSource(shifted, effectItemIdentityEffect: false);
        using Drawable.Resource materialized = CreateAliasingProneSource(shifted, effectItemIdentityEffect: true);

        using Bitmap expected = RenderProductionSource(direct);
        using Bitmap actual = RenderProductionSource(materialized);

        AssertCoverageParity(expected, actual, $"aliasing-prone shifted={shifted}");
    }

    [Test]
    public void ADrawableBrushRastersItsContentAtTheRequestedDensity()
    {
        using Drawable.Resource source = CreateDrawableBrushSource();
        using RenderNodeRasterization rasterization = RasterizeDrawableBrushContent(
            source,
            new Size(146, 82),
            density: 2f);

        Assert.Multiple(() =>
        {
            Assert.That(rasterization.IsEmpty, Is.False);
            Assert.That(GetNonBlackExtent(rasterization.Bitmap!), Is.Not.EqualTo(default(PixelRect)));
            Assert.That(rasterization.Bitmap!.Width, Is.EqualTo(rasterization.Bounds.Width * 2));
            Assert.That(rasterization.Bitmap!.Height, Is.EqualTo(rasterization.Bounds.Height * 2));
        });
    }

    [Test]
    public void AFallbackBrushDrawsNothingWithoutFailingTheFrame()
    {
        using Drawable.Resource source = CreateFallbackBrushSource();

        using Bitmap rendered = RenderProductionSource(source);

        Assert.That(
            GetNonBlackExtent(rendered),
            Is.EqualTo(default(PixelRect)),
            "A brush that could not be resolved must leave the frame untouched rather than paint a placeholder.");
    }


    private static RenderNodeRasterization RasterizeDrawableBrushContent(
        Drawable.Resource drawable,
        Size brushSize,
        float density)
    {
        using var node = new DrawableRenderNode(drawable);
        using (var context = new GraphicsContext2D(node, brushSize, density))
        {
            drawable.RequireOriginal().Render(context, drawable);
        }

        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Delivery,
            OutputScale = density,
            MaxWorkingScale = density,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            Purpose = RenderRequestPurpose.Auxiliary,
        });
        return renderer.Rasterize();
    }

    private static Bitmap RenderProductionSource(Drawable.Resource resource)
        => GoldenImageHarness.RenderAtScale(
            resource,
            s_frame,
            1f,
            clearColor: Colors.Transparent,
            maxWorkingScale: 1f,
            purpose: RenderRequestPurpose.Frame);

    private static Drawable.Resource CreateAliasingProneSource(bool shifted, bool effectItemIdentityEffect)
    {
        var shape = new RectShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.TransformOrigin.Value = RelativePoint.Center;
        shape.Width.Value = 150;
        shape.Height.Value = 18;
        shape.Fill.Value = Brushes.White;
        var transform = new TransformGroup();
        transform.Children.Add(new RotationTransform(27));
        if (shifted)
            transform.Children.Add(new TranslateTransform(0.25f, 0.5f));
        shape.Transform.Value = transform;
        if (effectItemIdentityEffect)
        {
            var effect = new TransformEffect();
            effect.Transform.Value = new MatrixTransform(Matrix.Identity);
            shape.FilterEffect.Value = effect;
        }

        return shape.ToResource(CompositionContext.Default);
    }

    private static Drawable.Resource CreateDrawableBrushSource()
    {
        var content = new RectShape();
        content.AlignmentX.Value = AlignmentX.Center;
        content.AlignmentY.Value = AlignmentY.Center;
        content.Width.Value = 76;
        content.Height.Value = 44;
        content.Fill.Value = Brushes.White;

        var brush = new DrawableBrush(content);
        brush.Stretch.Value = Stretch.Fill;
        brush.TileMode.Value = TileMode.None;
        brush.DestinationRect.Value = RelativeRect.Fill;

        var shape = new RectShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.TransformOrigin.Value = RelativePoint.Center;
        shape.Width.Value = 146;
        shape.Height.Value = 82;
        shape.Fill.Value = brush;
        var transform = new TransformGroup();
        transform.Children.Add(new RotationTransform(27));
        transform.Children.Add(new TranslateTransform(0.25f, 0.5f));
        shape.Transform.Value = transform;
        return shape.ToResource(CompositionContext.Default);
    }

    private static Drawable.Resource CreateFallbackBrushSource()
    {
        var shape = new RectShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.TransformOrigin.Value = RelativePoint.Center;
        shape.Width.Value = 146;
        shape.Height.Value = 82;
        shape.Fill.Value = new FallbackBrush();
        var transform = new TransformGroup();
        transform.Children.Add(new RotationTransform(27));
        transform.Children.Add(new TranslateTransform(0.25f, 0.5f));
        shape.Transform.Value = transform;
        return shape.ToResource(CompositionContext.Default);
    }

    private static void AssertCoverageParity(Bitmap expected, Bitmap actual, string scenario)
    {
        RgbaMaximumError maximum = ImageMetrics.MaximumAbsoluteErrorPerChannel(expected, actual);
        RgbaMaximumError edgeMaximum = ImageMetrics.EdgeBandMaximumAbsoluteErrorPerChannel(expected, actual);
        PixelRect expectedExtent = GetNonBlackExtent(expected);
        int fractionalReferencePixels = FusionBoundaryExecutionTestSupport.CountFractionalAlphaPixels(expected);
        TestContext.WriteLine(
            $"{scenario}: extent={GetNonBlackExtent(actual)}, max={maximum.Maximum:F6}, edge-max={edgeMaximum.Maximum:F6}");

        Assert.Multiple(() =>
        {
            Assert.That(actual.Width, Is.EqualTo(expected.Width));
            Assert.That(actual.Height, Is.EqualTo(expected.Height));
            Assert.That(expectedExtent, Is.Not.EqualTo(default(PixelRect)),
                "The antialiased reference must have nonempty visible coverage.");
            Assert.That(fractionalReferencePixels, Is.GreaterThan(0),
                "The antialiased reference must contain fractional-alpha edge coverage.");
            Assert.That(
                GetNonBlackExtent(actual),
                Is.EqualTo(expectedExtent),
                "Materialization must retain the complete antialiased coverage fringe.");
            Assert.That(maximum.Maximum, Is.LessThanOrEqualTo(0.02),
                "Materialization introduced a visible whole-frame pixel error.");
            Assert.That(edgeMaximum.Maximum, Is.LessThanOrEqualTo(0.02),
                "Materialization changed an antialiased edge pixel.");
        });
    }

    private static PixelRect GetNonBlackExtent(Bitmap bitmap)
    {
        int minX = bitmap.Width;
        int minY = bitmap.Height;
        int maxX = -1;
        int maxY = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.SKBitmap.GetPixel(x, y);
                if (color.Red <= 1 && color.Green <= 1 && color.Blue <= 1)
                    continue;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < minX)
            return default;
        return new PixelRect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }
}
