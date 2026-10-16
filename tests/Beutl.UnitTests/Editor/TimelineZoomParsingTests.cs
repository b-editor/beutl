using System.Globalization;
using Beutl.Editor.Components.Helpers;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public class TimelineZoomParsingTests
{
    // Beutl sets only CurrentUICulture, so CurrentCulture follows the OS.
    [TestCase("de-DE")]
    [TestCase("fr-FR")]
    [TestCase("en-US")]
    public void TryParseZoom_ReadsTheMenuLevelsInEveryCulture(string cultureName)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

            Assert.That(TimelineHelper.TryParseZoom("0.2", out float zoom), Is.True);
            Assert.That(zoom, Is.EqualTo(0.2f));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
