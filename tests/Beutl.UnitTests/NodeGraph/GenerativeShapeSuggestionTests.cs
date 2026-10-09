using Beutl.Media;
using Beutl.NodeGraph.Generative;

namespace Beutl.UnitTests.NodeGraph;

[TestFixture]
public sealed class GenerativeShapeSuggestionTests
{
    [TestCase(1920, 1080, ExpectedResult = "16:9")]
    [TestCase(1080, 1920, ExpectedResult = "9:16")]
    [TestCase(1000, 1000, ExpectedResult = "1:1")]
    public string TheAspectRatioNearestTheFrameIsSuggested(int width, int height)
        => GenerativeShapeSuggestion.NearestAspectRatio(["16:9", "1:1", "9:16"], new PixelSize(width, height), "16:9");

    [Test]
    public void AFrameWithoutASizeFallsBack()
    {
        Assert.That(GenerativeShapeSuggestion.NearestAspectRatio(["1:1", "16:9"], null, "16:9"), Is.EqualTo("16:9"));
        Assert.That(GenerativeShapeSuggestion.NearestAspectRatio(["1:1", "4:3"], null, "16:9"), Is.EqualTo("1:1"));
    }

    [TestCase(1920, 1080, ExpectedResult = "1080p")]
    [TestCase(1280, 720, ExpectedResult = "720p")]
    [TestCase(1080, 1920, ExpectedResult = "1080p")]
    [TestCase(854, 480, ExpectedResult = "720p")]
    [TestCase(3840, 2160, ExpectedResult = "1080p")]
    public string TheSmallestResolutionCoveringTheShortSideIsSuggested(int width, int height)
        => GenerativeShapeSuggestion.SuggestResolution(["720p", "1080p"], new PixelSize(width, height));

    [Test]
    public void ResolutionLabelsTheServicePublishesAreUnderstood()
    {
        Assert.That(GenerativeShapeSuggestion.SuggestResolution(["hd", "fhd", "4k"], new PixelSize(3840, 2160)), Is.EqualTo("4k"));
        Assert.That(GenerativeShapeSuggestion.SuggestResolution(["hd", "fhd", "4k"], new PixelSize(1920, 1080)), Is.EqualTo("fhd"));
        Assert.That(GenerativeShapeSuggestion.SuggestResolution(["odd", "720p"], new PixelSize(1920, 1080)), Is.EqualTo("720p"));
        Assert.That(GenerativeShapeSuggestion.SuggestResolution(["odd"], new PixelSize(1920, 1080)), Is.EqualTo("odd"));
    }

    [TestCase(3.0, ExpectedResult = 4)]
    [TestCase(4.0, ExpectedResult = 4)]
    [TestCase(4.5, ExpectedResult = 6)]
    [TestCase(30.0, ExpectedResult = 8)]
    public int TheShortestDurationCoveringASpanIsSuggested(double seconds)
        => GenerativeShapeSuggestion.SuggestDuration([8, 4, 6], TimeSpan.FromSeconds(seconds), 6);

    [Test]
    public void WithoutASpanThePreferredDurationIsSuggestedWhenOffered()
    {
        Assert.That(GenerativeShapeSuggestion.SuggestDuration([4, 6, 8], null, 6), Is.EqualTo(6));
        Assert.That(GenerativeShapeSuggestion.SuggestDuration([5, 10], null, 6), Is.EqualTo(5));
    }

    [Test]
    public void TheOutpaintCanvasAddsTheExpansionOnEverySide()
    {
        Assert.That(
            AiImageEditTasks.GetOutpaintDimensions(1000, 500, 25),
            Is.EqualTo((1500, 750, 250, 125)));
        Assert.That(
            AiImageEditTasks.GetOutpaintDimensions(2, 2, 10),
            Is.EqualTo((4, 4, 1, 1)));
    }

    [Test]
    public void ARequestCanBeMadeWithoutANode()
    {
        var request = new AiVideoGenerationNodeRequest("video.generate")
        {
            Prompt = "waves",
            DurationSeconds = 6,
            Resolution = "720p",
            AspectRatio = "16:9",
            RequestKeySeed = "seed",
            ParameterFingerprint = "parameters",
        };

        Assert.Multiple(() =>
        {
            Assert.That(request.Node, Is.Null);
            Assert.That(request.CatalogOperationId, Is.EqualTo("video.generate"));
        });
    }
}
