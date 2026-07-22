using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public sealed class OpaqueSourceCoverageTests
{
    private static readonly PixelSize s_frame = new(200, 200);

    [TestCase(1f, false)]
    [TestCase(2f, false)]
    [TestCase(4f, false)]
    [TestCase(1f, true)]
    [TestCase(2f, true)]
    [TestCase(4f, true)]
    public void RotatedVectorSource_PreservesDirectAntialiasedCoverage(float density, bool shifted)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource resource = CreateAliasingProneSource(shifted, legacyIdentityEffect: false);
            using Bitmap expected = RenderDirectSource(resource, density);
            using Bitmap actual = RenderProductionSource(resource, density, useRenderCache: false);

            AssertCoverageParity(expected, actual, $"density={density}, shifted={shifted}");
        });
    }

    [TestCase(1f)]
    [TestCase(2f)]
    public void LegacyIdentityFilterChain_PreservesDirectAntialiasedCoverage(float density)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource resource = CreateAliasingProneSource(
                shifted: true,
                legacyIdentityEffect: true);
            using Bitmap expected = RenderDirectSource(resource, density);
            using Bitmap actual = RenderProductionSource(resource, density, useRenderCache: false);

            AssertCoverageParity(expected, actual, $"legacy identity filter, density={density}");
        });
    }

    [Test]
    public void RenderCacheMissAndHit_PreserveFractionalAntialiasedCoverage()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            const float density = 2;
            using Drawable.Resource resource = CreateAliasingProneSource(
                shifted: true,
                legacyIdentityEffect: false);
            using Bitmap expected = RenderDirectSource(resource, density);
            using DrawableRenderNode node = CreateProductionNode(resource, density);
            node.Cache.ReportRenderCount(RenderNodeCache.Count);
            var diagnostics = new RenderPipelineDiagnosticsState();
            using var renderer = CreateRenderer(node, density, useRenderCache: true, diagnostics);
            using Bitmap miss = RenderWithRenderer(renderer, density);

            Assert.That(node.Cache.IsCached, Is.True, "The first request must publish the cache candidate.");

            using Bitmap hit = RenderWithRenderer(renderer, density);

            Assert.Multiple(() =>
            {
                Assert.That(diagnostics.Latest[RenderPipelineCounter.RenderCacheHits], Is.EqualTo(1));
                Assert.That(GetNonBlackExtent(hit), Is.EqualTo(GetNonBlackExtent(miss)));
            });
            AssertCoverageParity(expected, miss, "cache miss");
            AssertCoverageParity(expected, hit, "cache hit");
        });
    }

    [TestCase(1f)]
    [TestCase(2f)]
    public void RotatedDrawableBrushSource_PreservesDirectAntialiasedCoverage(float density)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource resource = CreateDrawableBrushSource();
            using Bitmap expected = RenderDirectSource(resource, density);
            using Bitmap actual = RenderProductionSource(resource, density, useRenderCache: false);

            AssertCoverageParity(expected, actual, $"rotated DrawableBrush, density={density}");
        });
    }

    [TestCase(1f)]
    [TestCase(2f)]
    public void RotatedFallbackBrushSource_PreservesTransparentCompatibility(float density)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Drawable.Resource resource = CreateFallbackBrushSource();
            using Bitmap expected = RenderDirectSource(resource, density);
            using Bitmap actual = RenderProductionSource(resource, density, useRenderCache: false);

            Assert.Multiple(() =>
            {
                Assert.That(GetNonBlackExtent(expected), Is.EqualTo(default(PixelRect)),
                    "The in-tree fallback for an unknown Brush resource is transparent.");
                Assert.That(GetNonBlackExtent(actual), Is.EqualTo(default(PixelRect)));
                Assert.That(
                    actual.GetPixelSpan<ushort>().SequenceEqual(expected.GetPixelSpan<ushort>()),
                    Is.True);
            });
        });
    }

    private static Bitmap RenderDirectSource(Drawable.Resource resource, float density)
    {
        var shape = (RectShape)resource.GetOriginal();
        var shapeResource = (RectShape.Resource)resource;
        Size frameSize = s_frame.ToSize(1);
        Size shapeSize = shape.MeasureInternal(frameSize, resource);
        Matrix transform = shape.GetTransformMatrix(frameSize, shapeSize, resource);

        int deviceWidth = (int)MathF.Ceiling(s_frame.Width * density);
        int deviceHeight = (int)MathF.Ceiling(s_frame.Height * density);
        using RenderTarget target = RenderTarget.Create(deviceWidth, deviceHeight)
                                    ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using var canvas = new ImmediateCanvas(target, density, logicalSize: frameSize);
        canvas.Clear();
        Geometry.Resource geometry = shapeResource.GetGeometry()
            ?? throw new InvalidOperationException("RectShape did not produce geometry.");
        using (canvas.PushTransform(transform))
        {
            if (shapeResource.Fill is DrawableBrush.Resource drawableBrush
                && drawableBrush.Drawable is { } drawable)
            {
                using RenderNodeRasterization content = RasterizeDrawableBrushContent(
                    drawable,
                    geometry.Bounds.Size,
                    density);
                Bitmap bitmap = content.Bitmap
                    ?? throw new InvalidOperationException("DrawableBrush content rasterized empty.");
                PixelRect deviceBounds = PixelRect.FromRect(content.Bounds, density);
                using SKImage image = SKImage.FromBitmap(bitmap.SKBitmap);
                using SKShader shader = image.ToShader(
                    SKShaderTileMode.Decal,
                    SKShaderTileMode.Decal,
                    SKSamplingOptions.Default,
                    SKMatrix.CreateScaleTranslation(
                        1f / density,
                        1f / density,
                        deviceBounds.X / density,
                        deviceBounds.Y / density));
                var fill = new ResolvedBrush(
                    shapeResource.Fill,
                    new BrushTileContent(shader, content.Bounds, EffectiveScale.At(density)));
                canvas.DrawGeometry(geometry, fill, ResolvedPen.Empty);
            }
            else
            {
                canvas.DrawGeometry(geometry, shapeResource.Fill, shapeResource.Pen);
            }
        }

        return target.Snapshot();
    }

    private static RenderNodeRasterization RasterizeDrawableBrushContent(
        Drawable.Resource drawable,
        Size brushSize,
        float density)
    {
        using var node = new DrawableRenderNode(drawable);
        using (var context = new GraphicsContext2D(node, brushSize, density))
        {
            drawable.GetOriginal().Render(context, drawable);
        }

        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                Intent = RenderIntent.Delivery,
                OutputScale = density,
                MaxWorkingScale = density,
                UseRenderCache = false,
                RenderPurpose = RenderRequestPurpose.Auxiliary,
            });
        return renderer.Rasterize();
    }

    private static Bitmap RenderProductionSource(
        Drawable.Resource resource,
        float density,
        bool useRenderCache)
    {
        using DrawableRenderNode node = CreateProductionNode(resource, density);
        using var renderer = CreateRenderer(node, density, useRenderCache, diagnostics: null);
        return RenderWithRenderer(renderer, density);
    }

    private static DrawableRenderNode CreateProductionNode(Drawable.Resource resource, float density)
    {
        var node = new DrawableRenderNode(resource);
        using var context = new GraphicsContext2D(node, s_frame.ToSize(1), density);
        resource.GetOriginal().Render(context, resource);
        return node;
    }

    private static RenderNodeRenderer CreateRenderer(
        RenderNode node,
        float density,
        bool useRenderCache,
        IRenderPipelineDiagnosticsState? diagnostics)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                Intent = RenderIntent.Delivery,
                TargetDomain = new Rect(default, s_frame.ToSize(1)),
                OutputScale = density,
                MaxWorkingScale = density,
                UseRenderCache = useRenderCache,
                RenderPurpose = RenderRequestPurpose.Frame,
                Diagnostics = diagnostics,
            });

    private static Bitmap RenderWithRenderer(RenderNodeRenderer renderer, float density)
    {
        int deviceWidth = (int)MathF.Ceiling(s_frame.Width * density);
        int deviceHeight = (int)MathF.Ceiling(s_frame.Height * density);
        using RenderTarget target = RenderTarget.Create(deviceWidth, deviceHeight)
                                    ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using var canvas = new ImmediateCanvas(target, density, logicalSize: s_frame.ToSize(1));
        canvas.Clear();
        renderer.Render(canvas);
        return target.Snapshot();
    }

    private static Drawable.Resource CreateAliasingProneSource(bool shifted, bool legacyIdentityEffect)
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 150;
        shape.Height.CurrentValue = 18;
        shape.Fill.CurrentValue = Brushes.White;
        var transform = new TransformGroup();
        transform.Children.Add(new RotationTransform(27));
        if (shifted)
            transform.Children.Add(new TranslateTransform(0.25f, 0.5f));
        shape.Transform.CurrentValue = transform;
        if (legacyIdentityEffect)
        {
            var effect = new TransformEffect();
            effect.Transform.CurrentValue = new MatrixTransform(Matrix.Identity);
            shape.FilterEffect.CurrentValue = effect;
        }

        return shape.ToResource(CompositionContext.Default);
    }

    private static Drawable.Resource CreateDrawableBrushSource()
    {
        var content = new RectShape();
        content.AlignmentX.CurrentValue = AlignmentX.Center;
        content.AlignmentY.CurrentValue = AlignmentY.Center;
        content.Width.CurrentValue = 76;
        content.Height.CurrentValue = 44;
        content.Fill.CurrentValue = Brushes.White;

        var brush = new DrawableBrush(content);
        brush.Stretch.CurrentValue = Stretch.Fill;
        brush.TileMode.CurrentValue = TileMode.None;
        brush.DestinationRect.CurrentValue = RelativeRect.Fill;

        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 146;
        shape.Height.CurrentValue = 82;
        shape.Fill.CurrentValue = brush;
        var transform = new TransformGroup();
        transform.Children.Add(new RotationTransform(27));
        transform.Children.Add(new TranslateTransform(0.25f, 0.5f));
        shape.Transform.CurrentValue = transform;
        return shape.ToResource(CompositionContext.Default);
    }

    private static Drawable.Resource CreateFallbackBrushSource()
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 146;
        shape.Height.CurrentValue = 82;
        shape.Fill.CurrentValue = new FallbackBrush();
        var transform = new TransformGroup();
        transform.Children.Add(new RotationTransform(27));
        transform.Children.Add(new TranslateTransform(0.25f, 0.5f));
        shape.Transform.CurrentValue = transform;
        return shape.ToResource(CompositionContext.Default);
    }

    private static void AssertCoverageParity(Bitmap expected, Bitmap actual, string scenario)
    {
        RgbaMaximumError maximum = ImageMetrics.MaximumAbsoluteErrorPerChannel(expected, actual);
        RgbaMaximumError edgeMaximum = ImageMetrics.EdgeBandMaximumAbsoluteErrorPerChannel(expected, actual);
        TestContext.WriteLine(
            $"{scenario}: extent={GetNonBlackExtent(actual)}, max={maximum.Maximum:F6}, edge-max={edgeMaximum.Maximum:F6}");

        Assert.Multiple(() =>
        {
            Assert.That(actual.Width, Is.EqualTo(expected.Width));
            Assert.That(actual.Height, Is.EqualTo(expected.Height));
            Assert.That(
                GetNonBlackExtent(actual),
                Is.EqualTo(GetNonBlackExtent(expected)),
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
