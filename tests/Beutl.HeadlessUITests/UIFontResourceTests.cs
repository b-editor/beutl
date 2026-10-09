using System.Globalization;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Beutl.Controls.Styling;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class UIFontResourceTests
{
    [AvaloniaTest]
    [TestCase("Beutl.ExceptionHandler", "ja-JP", "Noto Sans JP")]
    [TestCase("Beutl.ExceptionHandler", "zh-CN", "Noto Sans SC")]
    [TestCase("Beutl.ExceptionHandler", "ko-KR", "Noto Sans KR")]
    public void Helper_startup_applies_parent_language_before_resolving_fonts(
        string assemblyName, string cultureName, string expectedFamily)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo? previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Type helperFonts = Assembly.Load(assemblyName).GetType(typeof(UiFonts).FullName!)!;
            MethodInfo applyCulture = helperFonts.GetMethod("ApplyCultureArgument", BindingFlags.Static | BindingFlags.NonPublic)!;
            string[] args = ["--title", "Localized title", UiFonts.UiCultureArgument, cultureName, "--progress"];
            var remainingArgs = (string[])applyCulture.Invoke(null, [args])!;
            var options = (FontManagerOptions)helperFonts.GetMethod(nameof(UiFonts.CreateFontManagerOptions))!
                .Invoke(null, [CultureInfo.CurrentUICulture])!;
            var resources = (ResourceDictionary)AvaloniaXamlLoader.Load(
                new Uri($"avares://{assemblyName}/Styling/Fonts.axaml"))!;

            Assert.Multiple(() =>
            {
                Assert.That(CultureInfo.CurrentUICulture.Name, Is.EqualTo(cultureName));
                Assert.That(CultureInfo.DefaultThreadCurrentUICulture!.Name, Is.EqualTo(cultureName));
                Assert.That(remainingArgs, Is.EqualTo(new[] { "--title", "Localized title", "--progress" }));
                Assert.That(new FontFamily(options.DefaultFamilyName!).Name, Is.EqualTo(expectedFamily));
                Assert.That(((FontFamily)resources["BeutlUIFontFamily"]!).Name, Is.EqualTo(expectedFamily));
            });
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentUICulture = previousDefault;
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [AvaloniaTest]
    [TestCase("Beutl.ExceptionHandler")]
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
    [TestCase("ja-JP")]
    [TestCase("en-US")]
    [TestCase("zh-CN")]
    [TestCase("ko-KR")]
    public void Missing_macOS_keyboard_glyphs_use_a_monochrome_font(string cultureName)
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("macOS uses symbolic shortcut labels and provides Lucida Grande.");

        var family = UiFonts.GetFontFamily(CultureInfo.GetCultureInfo(cultureName));
        Assert.That(GetShapedFontFamilies("↩⇞⇟⇥⌃⌥⌫⎋", family),
            Is.Not.Empty.And.All.EqualTo("Lucida Grande"));
    }

    [AvaloniaTest]
    [TestCase("😀")]
    [TestCase("⌚")]
    public void macOS_keyboard_fallback_keeps_emoji_in_an_emoji_font(string emoji)
    {
        if (!OperatingSystem.IsMacOS())
            Assert.Ignore("The keyboard-symbol fallback is only configured on macOS.");

        Assert.That(GetShapedFontFamilies(emoji, UiFonts.DefaultFontFamily),
            Is.Not.Empty.And.All.Contains("Emoji"));
    }

    private static string[] GetShapedFontFamilies(string value, FontFamily family)
    {
        var text = new TextBlock { Text = value, FontFamily = family, FontSize = 24 };
        text.Measure(Size.Infinity);
        text.Arrange(new Rect(text.DesiredSize));
        return text.TextLayout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>()
            .Select(run => run.GlyphRun.GlyphTypeface.FamilyName).ToArray();
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
    [TestCase("Noto Sans")]
    [TestCase("Noto Sans JP")]
    [TestCase("Noto Sans SC")]
    [TestCase("Noto Sans KR")]
    public void UIFont_is_registered_with_the_rendering_engine(string familyName)
    {
        var family = new Beutl.Media.FontFamily(familyName);
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

    [AvaloniaTest]
    public void NotoColorEmoji_is_bundled_and_registered_with_the_rendering_engine()
    {
        var uri = new Uri("avares://Beutl.Controls/Assets/Fonts/NotoColorEmoji/NotoColorEmoji.ttf");
        Assert.Multiple(() =>
        {
            Assert.That(AssetLoader.Exists(uri), Is.True);
            Assert.That(Beutl.Media.FontManager.Instance.IsRegistered(new Beutl.Media.FontFamily("Noto Color Emoji")), Is.True);
        });
    }

    [AvaloniaTest]
    [TestCase("ja-JP", "Noto Sans JP", Beutl.Media.FontWeight.Regular)]
    [TestCase("zh-CN", "Noto Sans SC", Beutl.Media.FontWeight.Regular)]
    [TestCase("ko-KR", "Noto Sans KR", Beutl.Media.FontWeight.Regular)]
    [TestCase("en-US", "Noto Sans JP", Beutl.Media.FontWeight.Regular)]
    [TestCase("zh-CN", "Noto Sans SC", Beutl.Media.FontWeight.SemiBold)]
    [TestCase("ko-KR", "Noto Sans KR", Beutl.Media.FontWeight.SemiBold)]
    public void Text_fallback_uses_the_UI_language_order(
        string cultureName, string expectedFamily, Beutl.Media.FontWeight weight)
    {
        using (Stream font = typeof(UIFontResourceTests).Assembly.GetManifestResourceStream("ProjectFontSecondFixture.ttf")!)
            Beutl.Media.FontManager.Instance.AddFont(font);
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            App.RegisterBundledEngineFonts();
            using var text = new Beutl.Media.TextFormatting.FormattedText
            {
                Font = new Beutl.Media.FontFamily("Roboto"),
                Text = "骨",
                Size = 64f,
                Weight = weight,
            };
            using var expected = new Beutl.Media.TextFormatting.FormattedText
            {
                Font = new Beutl.Media.FontFamily(expectedFamily),
                Text = "骨",
                Size = 64f,
                Weight = weight,
            };
            Assert.Multiple(() =>
            {
                Assert.That(text.ToGeometries().Length, Is.EqualTo(1));
                Assert.That(text.ToGeometries()[0].Bounds, Is.EqualTo(expected.ToGeometries()[0].Bounds));
                Assert.That(text.ToGeometries()[0].Bounds.IsEmpty, Is.False);
            });
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
            App.RegisterBundledEngineFonts();
        }
    }
}
