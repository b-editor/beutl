using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Beutl.Controls.Styling;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class UIFontResourceTests
{
    [AvaloniaTest]
    [TestCase("Beutl.ExceptionHandler")]
    [TestCase("Beutl.WaitingDialog")]
    public void Helper_dialog_font_resources_resolve_their_linked_assets(string assemblyName)
    {
        var resources = (ResourceDictionary)AvaloniaXamlLoader.Load(
            new Uri($"avares://{assemblyName}/Styling/Fonts.axaml"))!;
        var family = (FontFamily)resources["BeutlUIFontFamily"]!;
        Assert.That(family.FamilyNames, Is.EqualTo(UiFonts.DefaultFontFamily.FamilyNames));

        foreach (string name in family.FamilyNames)
        {
            var embedded = new FontFamily($"avares://{assemblyName}/Assets/Fonts/{name.Replace(" ", "")}#{name}");
            foreach (FontWeight weight in new[] { FontWeight.Normal, FontWeight.Medium, FontWeight.SemiBold, FontWeight.Bold })
            {
                Assert.That(FontManager.Current.TryGetGlyphTypeface(
                    new Typeface(embedded, weight: weight), out GlyphTypeface? typeface), Is.True);
                Assert.That(typeface!.Weight, Is.EqualTo(weight));
            }
        }
    }

    [AvaloniaTest]
    public void ContentControlThemeFontFamily_uses_localized_noto_sans_with_other_languages()
    {
        object? resource = Application.Current!.FindResource("ContentControlThemeFontFamily");

        var fontFamily = resource as FontFamily;

        Assert.That(fontFamily, Is.Not.Null);

        string[] familyNames = fontFamily!.FamilyNames
            .Select(static name => name.ToString())
            .ToArray();
        Assert.That(familyNames, Is.EqualTo(UiFonts.DefaultFontFamily.FamilyNames));
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
            var uri = new Uri($"avares://Beutl.Controls/Assets/Fonts/NotoSansJP/{fileName}");

            Assert.That(AssetLoader.Exists(uri), Is.True);
        }
    }

    [AvaloniaTest]
    public void NotoSansJP_is_registered_with_the_rendering_engine()
    {
        var family = new Beutl.Media.FontFamily("Noto Sans JP");
        Beutl.Media.Typeface[] typefaces =
            [.. Beutl.Media.FontManager.Instance.GetTypefaces(family)];

        Assert.Multiple(() =>
        {
            Assert.That(typefaces.Select(typeface => typeface.Weight),
                Does.Contain(Beutl.Media.FontWeight.Regular));
            Assert.That(typefaces.Select(typeface => typeface.Weight),
                Does.Contain(Beutl.Media.FontWeight.Medium));
            Assert.That(typefaces.Select(typeface => typeface.Weight),
                Does.Contain(Beutl.Media.FontWeight.SemiBold));
            Assert.That(typefaces.Select(typeface => typeface.Weight),
                Does.Contain(Beutl.Media.FontWeight.Bold));
        });
    }
}
