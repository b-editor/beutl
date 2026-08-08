using System.Reflection;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.TextFormatting;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class TextRenderNodeTests
{
    [Test]
    public void Measure_UsesRasterBoundsForTheEmptinessGate()
    {
        using var text = new FormattedText
        {
            Font = TypefaceProvider.Typeface().FontFamily,
            Size = 48f,
            Text = "Raster footprint",
        };
        Rect rasterBounds = text.RasterBounds;

        // Current public font backends did not expose a glyph with a degenerate outline but a non-empty
        // hinted mask. Inject that valid measured-state relationship to isolate which published bound the
        // render node gates on; the source allocation still comes from the real measured RasterBounds.
        FieldInfo actualBoundsField = typeof(FormattedText).GetField(
            "_actualBounds",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        actualBoundsField.SetValue(
            text,
            new Rect(rasterBounds.X, rasterBounds.Y, 0, rasterBounds.Height));

        using var node = new TextRenderNode(text, Brushes.Resource.White, null);
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

        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(measurement.HasContributingValues, Is.True);
            Assert.That(measurement.OutputBounds, Is.EqualTo(rasterBounds));
        });
    }

    [Test]
    public void DrawableBrush_RecordsNestedContentAgainstSemanticBounds()
    {
        using var text = new FormattedText
        {
            Font = TypefaceProvider.Typeface().FontFamily,
            Size = 48f,
            Text = "A    ",
        };
        Rect semanticBounds = text.Bounds;
        _ = text.RasterBounds;
        var injectedActualBounds = new Rect(
            semanticBounds.X,
            semanticBounds.Y,
            semanticBounds.Width / 2,
            semanticBounds.Height / 2);
        FieldInfo actualBoundsField = typeof(FormattedText).GetField(
            "_actualBounds",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        actualBoundsField.SetValue(text, injectedActualBounds);
        var observedSizes = new List<Size>();
        var content = new TextBrushBoundsProbeDrawable(observedSizes);
        var brush = new DrawableBrush(content);
        using DrawableBrush.Resource brushResource = brush.ToResource(CompositionContext.Default);
        using var node = new TextRenderNode(text, brushResource, null);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });

        _ = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(text.ActualBounds.Size, Is.Not.EqualTo(semanticBounds.Size));
            Assert.That(observedSizes, Is.EqualTo(new[] { semanticBounds.Size }));
        });
    }
}

internal sealed partial class TextBrushBoundsProbeDrawable(ICollection<Size> observedSizes) : Drawable
{
    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        observedSizes.Add(availableSize);
        return new Size(1, 1);
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        context.DrawRectangle(new Rect(0, 0, 1, 1), Brushes.Resource.White, null);
    }
}
