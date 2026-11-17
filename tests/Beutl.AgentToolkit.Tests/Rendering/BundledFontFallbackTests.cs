using System.Globalization;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using Beutl.ProjectSystem;
using SkiaSharp;

namespace Beutl.AgentToolkit.Tests.Rendering;

// The standalone server's text fallback (#2724): a project font with Latin glyphs only, and text it lacks.
[NonParallelizable]
public sealed class BundledFontFallbackTests
{
    // Roboto, put in the project's resources/fonts; it has no CJK or emoji glyphs.
    private static readonly FontFamily s_projectFont = new("Roboto");
    private static readonly FontFamily s_emojiFont = new(BundledFonts.EmojiFamilyName);
    private const string Latin = "Ag";
    private const string Cjk = "\u6F22\u5B57";
    private const string Emoji = "\U0001F600";
    // MAN, ZWJ, WOMAN, ZWJ, GIRL: one family glyph.
    private const string ZwjSequence = "\U0001F468\u200D\U0001F469\u200D\U0001F467";

    private string _root = null!;
    private FileSessionSource? _source;

    [SetUp]
    public void OpenProjectWithLatinOnlyFont()
    {
        _root = Directory.CreateTempSubdirectory("beutl-agent-bundled-fonts-").FullName;
        string fontDirectory = Directory.CreateDirectory(Path.Combine(_root, "resources", "fonts")).FullName;
        using (Stream input = typeof(BundledFontFallbackTests).Assembly.GetManifestResourceStream("LatinOnlyFontFixture.ttf")!)
        using (Stream output = File.Create(Path.Combine(fontDirectory, "latin.ttf")))
            input.CopyTo(output);
        string path = Path.Combine(_root, "project.bep");
        using (var creator = new FileSessionSource(TestFonts.Manager))
            TestWorkspace.CreateFileSession(creator, new ProjectCreateOptions(path, 64, 64, new Rational(30), TimeSpan.FromSeconds(1))).Save(skipConflictCheck: true);
        _source = new FileSessionSource(TestFonts.Manager);
        _source.OpenProject(path);

        // As AddAgentToolkitServer does when the standalone server starts.
        BundledFontRegistration.EnsureRegistered(TestFonts.Manager);
    }

    [TearDown]
    public void CloseProject()
    {
        _source?.Dispose();
        Directory.Delete(_root, true);
    }

    [Test]
    public void BundledRegistrationIsIdempotentForEachIndependentHost()
    {
        var configuration = new Beutl.Configuration.FontConfig();
        configuration.FontDirectories.Clear();
        var first = new FontManager(configuration);
        var second = new FontManager(configuration);
        BundledFontRegistration.EnsureRegistered(first);
        BundledFontRegistration.EnsureRegistered(second);
        long revision = second.Revision;
        BundledFontRegistration.EnsureRegistered(second);
        Assert.Multiple(() =>
        {
            Assert.That(first.GetTypefaces(new FontFamily("Noto Sans JP")), Is.Not.Empty);
            Assert.That(second.GetTypefaces(new FontFamily("Noto Sans JP")), Is.Not.Empty);
            Assert.That(second.Revision, Is.EqualTo(revision));
        });
    }

    [Test]
    public void Standalone_server_falls_back_to_the_UI_families_in_language_order_then_emoji()
    {
        (FontFamily[] families, FontFamily? emoji) = TestFonts.Manager.GetFallbackFamilies();
        string[] expected = BundledFonts.GetUiFamilyNames(CultureInfo.CurrentUICulture);

        Assert.Multiple(() =>
        {
            Assert.That(families.Select(family => family.Name), Is.EqualTo(expected));
            Assert.That(emoji, Is.EqualTo(s_emojiFont));
            Assert.That(TestFonts.Manager.IsRegistered(s_emojiFont), Is.True);
            foreach (string name in expected)
            {
                Assert.That(TestFonts.Manager.GetTypefaces(new FontFamily(name)).Select(typeface => typeface.Weight),
                    Is.SupersetOf(new[] { FontWeight.Regular, FontWeight.Medium, FontWeight.SemiBold, FontWeight.Bold }), name);
            }
        });
    }

    [TestCase(FontWeight.Regular)]
    [TestCase(FontWeight.Bold)]
    public void Project_font_keeps_its_glyphs_and_the_rest_falls_back_at_the_requested_weight(FontWeight weight)
    {
        var uiFont = new FontFamily(BundledFonts.GetUiFamilyNames(CultureInfo.CurrentUICulture)[0]);
        Assert.That(TestFonts.Manager.IsRegistered(s_projectFont), Is.True);

        using FormattedText latin = CreateText(Latin, s_projectFont, weight);
        using FormattedText latinInUiFont = CreateText(Latin, uiFont, weight);
        using FormattedText mixed = CreateText(Latin + Cjk, s_projectFont, weight);
        using FormattedText cjk = CreateText(Cjk, s_projectFont, weight);
        using FormattedText cjkInUiFont = CreateText(Cjk, uiFont, weight);
        using FormattedText cjkInUiFontRegular = CreateText(Cjk, uiFont, FontWeight.Regular);
        using FormattedText emoji = CreateText(Emoji, s_projectFont, weight);
        using FormattedText emojiInEmojiFont = CreateText(Emoji, s_emojiFont, weight);
        using FormattedText zwj = CreateText(ZwjSequence, s_projectFont, weight);
        using FormattedText zwjInEmojiFont = CreateText(ZwjSequence, s_emojiFont, weight);

        Assert.Multiple(() =>
        {
            // The project's font draws what it has, rather than the fallback.
            Assert.That(latin.Bounds, Is.Not.EqualTo(latinInUiFont.Bounds));
            Assert.That(mixed.Bounds.Width, Is.EqualTo(latin.Bounds.Width + cjk.Bounds.Width).Within(0.001f));
            // The rest uses the UI language's family at the requested weight.
            Assert.That(GlyphBounds(cjk), Has.Length.EqualTo(Cjk.Length).And.EqualTo(GlyphBounds(cjkInUiFont)));
            if (weight != FontWeight.Regular)
                Assert.That(GlyphBounds(cjk), Is.Not.EqualTo(GlyphBounds(cjkInUiFontRegular)));
            Assert.That(emoji.Bounds, Is.EqualTo(emojiInEmojiFont.Bounds));
            // The ZWJ sequence stays one glyph of the emoji font, as wide as a single emoji.
            Assert.That(zwj.Bounds, Is.EqualTo(zwjInEmojiFont.Bounds));
            Assert.That(zwj.Bounds.Width, Is.EqualTo(emoji.Bounds.Width).Within(0.001f));
        });
    }

    [Test]
    public async Task Render_still_draws_CJK_and_color_emoji_in_a_Latin_only_project_font()
    {
        string dir = TestWorkspace.CreateDirectory();
        var scene = new Scene(640, 160, "bundled-fonts")
        {
            Duration = { Value = MediaTime.FromSeconds(1) },
            Uri = new Uri(Path.Combine(dir, "Scene.scene"))
        };
        var element = new Element
        {
            Length = { Value = MediaTime.FromSeconds(1) },
            Uri = new Uri(Path.Combine(dir, "element.belm"))
        };
        element.AddObject(new TextBlock
        {
            Text = { Value = $"{Latin} {Cjk} {Emoji} {ZwjSequence}" },
            FontFamily = { Value = s_projectFont },
            Size = { Value = 64 },
            Fill = { Value = new SolidColorBrush(Colors.White) }
        });
        scene.Children.Add(element);
        string output = Path.Combine(dir, "bundled-fonts.png");

        await new StillRenderer(TestFonts.Manager).RenderAsync(scene, MediaTime.Zero, output, 1, CancellationToken.None);

        using SKBitmap bitmap = SKBitmap.Decode(output);
        int opaque = 0;
        int colored = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor color = bitmap.GetPixel(x, y);
                if (color.Alpha < 200)
                    continue;
                opaque++;
                if (Math.Max(color.Red, Math.Max(color.Green, color.Blue)) - Math.Min(color.Red, Math.Min(color.Green, color.Blue)) > 80)
                    colored++;
            }
        }

        TestContext.Out.WriteLine($"{output}: {opaque} opaque, {colored} colored pixels");
        Assert.Multiple(() =>
        {
            Assert.That(opaque, Is.GreaterThan(2000));
            // The white text has no saturated pixels of its own; the color emoji do.
            Assert.That(colored, Is.GreaterThan(500));
        });
    }

    private static Graphics.Rect[] GlyphBounds(FormattedText text)
    {
        var bounds = new List<Graphics.Rect>();
        foreach (var geometry in text.ToGeometries())
            bounds.Add(geometry.Bounds);
        return [.. bounds];
    }

    private static FormattedText CreateText(string text, FontFamily font, FontWeight weight)
    {
        return new FormattedText(TestFonts.Manager) { Font = font, Text = text, Size = 64f, Weight = weight };
    }
}
