using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using Beutl.Controls.Styling;
using Beutl.Testing.Headless;

namespace Beutl.E2ETests.Controls;

[TestFixture]
public class UiFontsTests
{
    private const string MixedText = "English · Español: áéíóú ñ · 日本語 · 简体中文 华 · 한국어 한글 · ɖ";
    private static readonly FontWeight[] s_weights =
        [FontWeight.Normal, FontWeight.Medium, FontWeight.SemiBold, FontWeight.Bold];

    [TestCase("en-US", "Noto Sans")]
    [TestCase("es", "Noto Sans")]
    [TestCase("es-MX", "Noto Sans")]
    [TestCase("ja", "Noto Sans JP")]
    [TestCase("ja-JP", "Noto Sans JP")]
    [TestCase("zh-CN", "Noto Sans SC")]
    [TestCase("ko-KR", "Noto Sans KR")]
    [TestCase("de-DE", "Noto Sans")]
    [TestCase("", "Noto Sans")]
    public void Selected_language_is_first_and_other_bundled_languages_remain_fallbacks(
        string cultureName, string primaryFamily)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        FontFamily family = UiFonts.GetFontFamily(culture);
        FontManagerOptions options = UiFonts.CreateFontManagerOptions(culture);

        Assert.Multiple(() =>
        {
            Assert.That(family.Name, Is.EqualTo(primaryFamily));
            Assert.That(family.FamilyNames, Is.EquivalentTo(
                new[] { "Noto Sans", "Noto Sans JP", "Noto Sans SC", "Noto Sans KR" }));
            Assert.That(new FontFamily(options.DefaultFamilyName!).FamilyNames, Is.EqualTo(family.FamilyNames));
            Assert.That(options.FontFallbacks!.Select(fallback => fallback.FontFamily.Name),
                Is.EqualTo(family.FamilyNames));
        });
    }

    [AvaloniaTest]
    [TestCase("en-US", "Noto Sans")]
    [TestCase("es", "Noto Sans")]
    [TestCase("ja-JP", "Noto Sans JP")]
    [TestCase("zh-CN", "Noto Sans SC")]
    [TestCase("ko-KR", "Noto Sans KR")]
    public void Embedded_fonts_resolve_at_each_weight_and_cover_other_languages(
        string cultureName, string primaryFamily)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        FontManager manager = FontManager.Current;
        FontFamily selectedFamily = UiFonts.GetFontFamily(culture);
        foreach (FontWeight weight in s_weights)
        {
            var requested = new Typeface(selectedFamily, weight: weight);
            Assert.That(manager.TryGetGlyphTypeface(requested, out GlyphTypeface? primary), Is.True);
            GlyphTypeface primaryTypeface = primary!;
            Assert.Multiple(() =>
            {
                Assert.That(GetFamilyName(primaryTypeface), Is.EqualTo(primaryFamily));
                Assert.That(primaryTypeface.Weight, Is.EqualTo(weight));
                Assert.That(primaryTypeface.FontSimulations, Is.EqualTo(FontSimulations.None));
            });

            // Each of these characters is absent from the other three bundled families.
            foreach ((char character, string expected) in new[]
                     { ('ɖ', "Noto Sans"), ('华', "Noto Sans SC"), ('한', "Noto Sans KR") })
            {
                Assert.That(manager.TryMatchCharacter(character, FontStyle.Normal, weight, FontStretch.Normal,
                    new FontFamily("Monospace"), culture, out Typeface fallback), Is.True);
                Assert.That(fallback.FontFamily.Name, Is.EqualTo(expected));
                Assert.That(manager.TryGetGlyphTypeface(fallback, out GlyphTypeface? glyphTypeface), Is.True);
                Assert.That(glyphTypeface!.Weight, Is.EqualTo(weight));
                Assert.That(glyphTypeface.CharacterToGlyphMap.TryGetGlyph(character, out ushort glyph), Is.True);
                Assert.That(glyph, Is.Not.Zero);
            }
        }
    }

    [AvaloniaTest]
    [TestCase("en-US", false)]
    [TestCase("en-US", true)]
    [TestCase("es", false)]
    [TestCase("es", true)]
    [TestCase("ja-JP", false)]
    [TestCase("ja-JP", true)]
    [TestCase("zh-CN", false)]
    [TestCase("zh-CN", true)]
    [TestCase("ko-KR", false)]
    [TestCase("ko-KR", true)]
    public void Shared_font_resources_render_mixed_language_controls_without_missing_glyphs(
        string cultureName, bool light)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        Window? window = null;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            var fontResources = (ResourceDictionary)AvaloniaXamlLoader.Load(
                new Uri("avares://Beutl.Controls/Styling/Fonts.axaml"))!;
            var family = (FontFamily)fontResources["BeutlUIFontFamily"]!;
            var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
            panel.Children.Add(new TextBlock
            {
                Text = $"{CultureInfo.CurrentUICulture.NativeName} · {family.Name}",
                FontFamily = family,
                FontSize = 24,
                FontWeight = FontWeight.SemiBold,
            });
            TextBlock[] samples = s_weights.Select(weight => new TextBlock
            {
                Text = MixedText,
                FontFamily = family,
                FontSize = 18,
                FontWeight = weight,
                TextWrapping = TextWrapping.Wrap,
            }).ToArray();
            foreach (TextBlock sample in samples)
                panel.Children.Add(sample);
            panel.Children.Add(new TextBox { Text = MixedText, FontFamily = family });
            panel.Children.Add(new Button { Content = "Save · 保存 · 저장 · Guardar", FontFamily = family });
            window = new Window
            {
                Width = 780,
                Height = 460,
                Content = panel,
                RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark,
            };
            window.Resources.MergedDictionaries.Add(fontResources);
            window.Show();
            HeadlessTestHelpers.Render(3);
            Assert.That(family, Is.EqualTo(UiFonts.GetFontFamily(CultureInfo.CurrentUICulture)));
            foreach (TextBlock sample in samples)
            {
                ShapedTextRun[] runs = sample.TextLayout!.TextLines
                    .SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().ToArray();
                Assert.That(runs, Is.Not.Empty);
                Assert.That(runs.SelectMany(run => run.GlyphRun.GlyphInfos)
                    .All(glyph => glyph.GlyphIndex != 0), Is.True, "Mixed-language text must not render .notdef.");
                Assert.That(runs.Select(run => GetFamilyName(run.GlyphRun.GlyphTypeface)).Distinct(),
                    Is.SubsetOf(family.FamilyNames));
            }

            if (Environment.GetEnvironmentVariable("BEUTL_UI_FONTS_CAPTURE_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                Assert.That(frame, Is.Not.Null);
                frame!.Save(Path.Combine(directory, $"{cultureName}-{(light ? "light" : "dark")}.png"),
                    PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            try
            {
                window?.Close();
                HeadlessTestHelpers.Settle();
            }
            finally
            {
                CultureInfo.CurrentUICulture = previous;
            }
        }
    }

    private static string GetFamilyName(GlyphTypeface typeface) =>
        string.IsNullOrEmpty(typeface.TypographicFamilyName) ? typeface.FamilyName : typeface.TypographicFamilyName;
}
