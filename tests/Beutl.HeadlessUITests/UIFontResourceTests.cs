using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Platform;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class UIFontResourceTests
{
    [AvaloniaTest]
    public void ContentControlThemeFontFamily_uses_noto_sans_jp()
    {
        object? resource = Application.Current!.FindResource("ContentControlThemeFontFamily");

        var fontFamily = resource as FontFamily;

        Assert.That(fontFamily, Is.Not.Null);

        string[] familyNames = fontFamily!.FamilyNames
            .Select(static name => name.ToString())
            .ToArray();
        Assert.That(familyNames, Does.Contain("Noto Sans JP"));
    }

    [AvaloniaTest]
    public void NotoSansJP_font_assets_are_embedded()
    {
        string[] fileNames =
        [
            "NotoSansJP-Regular.ttf",
            "NotoSansJP-Medium.ttf",
            "NotoSansJP-SemiBold.ttf",
            "NotoSansJP-Bold.ttf",
        ];

        foreach (string fileName in fileNames)
        {
            var uri = new Uri($"avares://Beutl.Controls/Assets/Fonts/{fileName}");

            Assert.That(AssetLoader.Exists(uri), Is.True);
        }
    }
}
