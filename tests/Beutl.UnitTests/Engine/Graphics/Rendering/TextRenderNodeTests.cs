using System.Reflection;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using static Beutl.UnitTests.Engine.Graphics.Rendering.RenderGraphTestSupport;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public class TextRenderNodeTests
{
    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void PublishedBounds_AreTheTextsOwnBoundsAtEveryOutputScale(float outputScale)
    {
        using FormattedText text = CreateText();
        using var node = new TextRenderNode(text, Brushes.Resource.White, null);
        using var owner = new RenderRequestOwner();
        using var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: outputScale,
            maxWorkingScale: outputScale,
            owner: owner));

        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);

        Assert.Multiple(() =>
        {
            Assert.That(GetSingleRoot(graph).RecordedBounds, Is.EqualTo(text.ActualBounds));
            Assert.That(text.GetRasterBounds(outputScale), Is.Not.EqualTo(text.ActualBounds),
                "The fixture must exercise a density whose mask reaches outside the text's own bounds.");
        });
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void DeclaredRasterOutset_CoversTheMaskAtTheRecordedScale(float outputScale)
    {
        using FormattedText text = CreateText();
        using var node = new TextRenderNode(text, Brushes.Resource.White, null);
        using var owner = new RenderRequestOwner();
        using var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: outputScale,
            maxWorkingScale: outputScale,
            owner: owner));

        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
        RenderFragmentReference root = GetSingleRoot(graph);
        var payload = (OpaqueRenderFragmentPayload)root.Payload!;
        Rect footprint = root.RecordedBounds.Inflate(payload.Description.Bounds.RasterOutset);

        Assert.That(
            footprint.Contains(text.GetRasterBounds(outputScale)),
            Is.True,
            $"The buffer must still clear the glyph masks measured at {outputScale}.");
    }

    private static FormattedText CreateText()
        => new()
        {
            Font = TypefaceProvider.Typeface().FontFamily,
            Size = 48f,
            Text = "Raster footprint",
        };

    [Test]
    public void Measure_UsesRasterBoundsForTheEmptinessGate()
    {
        using FormattedText text = CreateText();
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
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
        });
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(measurement.HasContributingValues, Is.True);
            Assert.That(measurement.OutputBounds, Is.EqualTo(rasterBounds));
        });
    }

}
