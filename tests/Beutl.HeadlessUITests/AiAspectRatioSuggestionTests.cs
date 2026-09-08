using Beutl.Media;
using Beutl.ViewModels.Dialogs;
using NUnit.Framework;

namespace Beutl.HeadlessUITests;

/// <summary>
/// The dialog offers the shape the scene is in. Before ratios existed the
/// choices were three fixed sizes with no 16:9 at all, so a widescreen project
/// was handed 3:2 and the generated asset never fitted the frame.
/// </summary>
public class AiAspectRatioSuggestionTests
{
    private static readonly string[] s_imageRatios = ["16:9", "1:1", "9:16", "4:3", "3:4"];

    [TestCase(1920, 1080, "16:9")]
    [TestCase(3840, 2160, "16:9")]
    [TestCase(1080, 1920, "9:16")]
    [TestCase(1080, 1080, "1:1")]
    [TestCase(1440, 1080, "4:3")]
    [TestCase(1080, 1440, "3:4")]
    public void Nearest_MatchesTheSceneTheAssetIsMadeFor(int width, int height, string expected)
    {
        Assert.That(
            AiAspectRatioSuggestion.Nearest(s_imageRatios, new PixelSize(width, height), "16:9"),
            Is.EqualTo(expected));
    }

    [Test]
    public void Nearest_TreatsAPortraitSceneAsFarFromWideAsALandscapeOne()
    {
        // Measured in log space: a linear difference would put 9:16 closer to
        // 16:9 than 1:1 is, and every vertical project would be offered a
        // widescreen image.
        Assert.That(
            AiAspectRatioSuggestion.Nearest(["16:9", "1:1", "9:16"], new PixelSize(1080, 1920), "16:9"),
            Is.EqualTo("9:16"));
    }

    [Test]
    public void Nearest_FallsBackWhenThereIsNoSceneToMatch()
    {
        Assert.That(
            AiAspectRatioSuggestion.Nearest(s_imageRatios, null, "16:9"),
            Is.EqualTo("16:9"));
        Assert.That(
            AiAspectRatioSuggestion.Nearest(s_imageRatios, new PixelSize(0, 0), "16:9"),
            Is.EqualTo("16:9"));
    }

    [Test]
    public void Nearest_ChoosesFromOnlyWhatIsOffered()
    {
        // The video dialog offers two; a 4:3 scene still has to land on one.
        Assert.That(
            AiAspectRatioSuggestion.Nearest(["16:9", "9:16"], new PixelSize(1440, 1080), "16:9"),
            Is.EqualTo("16:9"));
    }
}
