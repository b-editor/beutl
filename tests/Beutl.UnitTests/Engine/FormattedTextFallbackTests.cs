using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using SkiaSharp;

namespace Beutl.UnitTests.Engine;

[TestFixture]
[NonParallelizable]
public class FormattedTextFallbackTests
{
    private static readonly FontFamily s_uiFont = new("Noto Sans JP");
    private static readonly FontFamily s_emojiFont = new("Noto Color Emoji");
    private static readonly FontFamily s_chineseFont = new("Noto Sans SC");
    // NotoSansJP-Regular.otf subset to U+0065 and U+00E9, without U+0301 in cmap.
    // Renamed under the adjacent BeutlTestComposedText-LICENSE.txt (SIL OFL 1.1).
    private static readonly FontFamily s_composedFont = new("Beutl Test Composed Text");
    private FontFamily[] _previousFamilies = [];
    private FontFamily? _previousEmoji;

    [OneTimeSetUp]
    public void RegisterFonts()
    {
        _ = TypefaceProvider.Typeface();
        foreach (string file in new[] { "NotoColorEmoji.ttf", "NotoSansSC-Regular.otf" })
        {
            using Stream stream = typeof(FormattedTextFallbackTests).Assembly.GetManifestResourceStream(file)!;
            FontManager.Instance.AddFont(stream);
        }
    }

    [SetUp]
    public void SetFallbacks()
    {
        var previous = FontManager.Instance.GetFallbackFamilies();
        _previousFamilies = previous.Families;
        _previousEmoji = previous.Emoji;
        FontManager.Instance.SetFallbackFonts([s_uiFont, s_chineseFont], s_emojiFont);
    }

    [TearDown]
    public void RestoreFallbacks() => FontManager.Instance.SetFallbackFonts(_previousFamilies, _previousEmoji);

    [TestCase("AV日本ffi", "AV", "日本", "ffi")]
    [TestCase("AVか\u3099ffi", "AV", "か\u3099", "ffi")]
    public void MissingGlyphs_MatchExplicitUIRunsAndPreserveSelectedFont(
        string value, string prefix, string fallback, string suffix)
    {
        using FormattedText mixed = CreateText(value);
        using FormattedText first = CreateText(prefix);
        using FormattedText middle = CreateText(fallback, s_uiFont);
        using FormattedText last = CreateText(suffix);
        using SKBitmap actual = Draw(mixed);
        using var surface = SKSurface.Create(new SKImageInfo(900, 300));
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.Translate(16, 100);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        float x = 0;
        foreach (FormattedText part in new[] { first, middle, last })
        {
            surface.Canvas.DrawText(part.GetTextBlob(), x, 0, paint);
            x += part.Bounds.Width;
        }
        using SKImage image = surface.Snapshot();
        using SKBitmap expected = SKBitmap.FromImage(image);

        Assert.Multiple(() =>
        {
            Assert.That(actual.Bytes, Is.EqualTo(expected.Bytes));
            Assert.That(mixed.Bounds.Width, Is.EqualTo(x).Within(0.001f));
            Assert.That(mixed.ToGeometries().Length,
                Is.EqualTo(first.ToGeometries().Length + middle.ToGeometries().Length + last.ToGeometries().Length));
            Assert.That(mixed.Metrics.Ascent, Is.LessThanOrEqualTo(middle.Metrics.Ascent));
            Assert.That(mixed.Metrics.Descent, Is.GreaterThanOrEqualTo(middle.Metrics.Descent));
        });
    }

    [TestCase(FontWeight.Regular)]
    [TestCase(FontWeight.Bold)]
    public void FallbackFont_UsesRequestedWeight(FontWeight weight)
    {
        using FormattedText text = CreateText("日本");
        using FormattedText expected = CreateText("日本", s_uiFont);
        text.Weight = expected.Weight = weight;
        using SKBitmap actualImage = Draw(text);
        using SKBitmap expectedImage = Draw(expected);
        Assert.That(actualImage.Bytes, Is.EqualTo(expectedImage.Bytes));
    }

    [TestCase("e\u0301", "é")]
    [TestCase("ee\u0301é", "eéé")]
    public void CombiningSequence_WithoutStandaloneMark_KeepsTheSelectedFont(string decomposed, string composed)
    {
        using FormattedText text = CreateText(decomposed, s_composedFont);
        using FormattedText expected = CreateText(composed, s_composedFont);
        using SKFont font = text.ToSKFont();
        Assert.Multiple(() =>
        {
            Assert.That(font.ContainsGlyph('e'), Is.True);
            Assert.That(font.ContainsGlyph('é'), Is.True);
            Assert.That(font.ContainsGlyph(0x0301), Is.False);
        });
        List<TextFontFallback.Run> runs = TextFontFallback.GetRuns(text.Text.AsSpan(), font, text.Style, text.Weight);
        Assert.Multiple(() =>
        {
            Assert.That(runs, Has.Count.EqualTo(1));
            Assert.That(runs[0].Typeface.FamilyName, Is.EqualTo(s_composedFont.Name));
            Assert.That(text.Bounds, Is.EqualTo(expected.Bounds));
        });
        using SKBitmap actualImage = Draw(text);
        using SKBitmap expectedImage = Draw(expected);
        Assert.That(actualImage.Bytes, Is.EqualTo(expectedImage.Bytes));
    }

    [Test]
    public void CombiningSequence_WithMissingComposite_StillUsesUIFallback()
    {
        using FormattedText text = CreateText("e\u0300", s_composedFont);
        using FormattedText expected = CreateText("e\u0300", s_uiFont);
        using SKFont font = text.ToSKFont();
        Assert.That(font.ContainsGlyph('è'), Is.False);
        List<TextFontFallback.Run> runs = TextFontFallback.GetRuns(text.Text.AsSpan(), font, text.Style, text.Weight);
        Assert.That(runs[0].Typeface.FamilyName, Is.EqualTo(s_uiFont.Name));
        using SKBitmap actualImage = Draw(text);
        using SKBitmap expectedImage = Draw(expected);
        Assert.That(actualImage.Bytes, Is.EqualTo(expectedImage.Bytes));
    }

    [TestCase("\u5140\uFE00", false)]
    [TestCase("\u3402\U000E0100", true)]
    public void VariationSequence_UsesTheFontWithTheRequestedVariant(string value, bool supplementary)
    {
        FontFamily primaryFamily = supplementary ? s_chineseFont : s_uiFont;
        FontFamily fallbackFamily = supplementary ? s_uiFont : s_chineseFont;
        FontManager.Instance.SetFallbackFonts([fallbackFamily], s_emojiFont);
        using FormattedText text = CreateText(value, primaryFamily);
        using FormattedText expected = CreateText(value, fallbackFamily);
        using SKFont primary = text.ToSKFont();
        using SKFont fallback = expected.ToSKFont();
        using var primaryShaper = new TextShaper(primary.Typeface);
        using var fallbackShaper = new TextShaper(fallback.Typeface);
        int codepoint = supplementary ? 0x3402 : 0x5140;
        uint selector = supplementary ? 0xE0100u : 0xFE00u;
        Assert.Multiple(() =>
        {
            Assert.That(primary.ContainsGlyph(codepoint), Is.True);
            Assert.That(primaryShaper.HasVariationGlyph(codepoint, selector), Is.False);
            Assert.That(fallbackShaper.HasVariationGlyph(codepoint, selector), Is.True);
        });
        List<TextFontFallback.Run> runs = TextFontFallback.GetRuns(value.AsSpan(), primary, text.Style, text.Weight);
        Assert.That(runs, Has.Count.EqualTo(1));
        Assert.That(runs[0].Typeface.FamilyName, Is.EqualTo(fallbackFamily.Name));
        using SKBitmap actualImage = Draw(text);
        using SKBitmap expectedImage = Draw(expected);
        Assert.That(actualImage.Bytes, Is.EqualTo(expectedImage.Bytes));
    }

    [TestCase("0\uFE0F")]
    [TestCase("1\uFE0F")]
    [TestCase("2\uFE0F")]
    [TestCase("3\uFE0F")]
    [TestCase("4\uFE0F")]
    [TestCase("5\uFE0F")]
    [TestCase("6\uFE0F")]
    [TestCase("7\uFE0F")]
    [TestCase("8\uFE0F")]
    [TestCase("9\uFE0F")]
    [TestCase("#\uFE0F")]
    [TestCase("*\uFE0F")]
    [TestCase("😀")]
    [TestCase("\U0001F3FB")]
    [TestCase("\U0001F3FC")]
    [TestCase("\U0001F3FD")]
    [TestCase("\U0001F3FE")]
    [TestCase("\U0001F3FF")]
    [TestCase("👩‍💻")]
    [TestCase("👨‍👩‍👧‍👦")]
    [TestCase("👍🏽")]
    [TestCase("🇯🇵")]
    [TestCase("1️⃣")]
    [TestCase("©️")]
    public void Emoji_UsesBundledColorFontAndKeepsTheSequenceTogether(string value)
    {
        using FormattedText text = CreateText(value);
        Assert.That(text.ToGeometries().Length, Is.EqualTo(1));
        FormattedText glyph = text.GetNonOutlineGlyph(0)!;
        Assert.That(glyph, Is.Not.Null);
        using SKFont font = glyph.ToSKFont();
        using SKBitmap image = Draw(text);
        Assert.Multiple(() =>
        {
            Assert.That(font.Typeface.FamilyName, Is.EqualTo(s_emojiFont.Name));
            Assert.That(image.Pixels.Count(pixel => pixel != SKColors.White), Is.GreaterThan(100));
        });
    }

    [TestCase("\u2600\uFE0E")]
    [TestCase("©\uFE0E")]
    [TestCase("1\uFE0E")]
    public void TextPresentation_WithAnEmojiOnlySelectedFont_UsesTextFallback(string value)
    {
        using FormattedText text = CreateText(value, s_emojiFont);
        using FormattedText expected = CreateText(value, s_uiFont);
        using SKFont primary = text.ToSKFont();
        using var shaper = new TextShaper(primary.Typeface);
        Assert.Multiple(() =>
        {
            Assert.That(primary.ContainsGlyph(value[0]), Is.True);
            Assert.That(shaper.HasVariationGlyph(value[0], 0xFE0F), Is.True);
            Assert.That(shaper.HasVariationGlyph(value[0], 0xFE0E), Is.False);
        });
        List<TextFontFallback.Run> runs = TextFontFallback.GetRuns(value.AsSpan(), primary, text.Style, text.Weight);
        Assert.That(runs, Has.Count.EqualTo(1));
        Assert.That(runs[0].Typeface.FamilyName, Is.EqualTo(s_uiFont.Name));
        using SKBitmap actualImage = Draw(text);
        using SKBitmap expectedImage = Draw(expected);
        Assert.Multiple(() =>
        {
            Assert.That(ColorPixels(actualImage), Is.Empty);
            Assert.That(actualImage.Bytes, Is.EqualTo(expectedImage.Bytes));
        });
    }

    [TestCase("123 #* ©")]
    [TestCase("1\uFE0E #\uFE0E *\uFE0E")]
    [TestCase("©\uFE0E")]
    public void OrdinaryDigitsAndTextPresentation_KeepTheSelectedFont(string value)
    {
        using FormattedText text = CreateText(value);
        Assert.That(text.GetFillPath().IsEmpty, Is.False);
        for (int i = 0; i < text.ToGeometries().Length; i++)
            Assert.That(text.GetNonOutlineGlyph(i), Is.Null);
        using SKBitmap image = Draw(text);
        Assert.That(ColorPixels(image), Is.Empty);
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void MixedFallbacks_PreserveSpacingAndSplitColorGlyphsAtEveryDensity(float scale)
    {
        using FormattedText text = CreateText("AV日本😀👩‍💻AV");
        text.Spacing = 7f;
        Assert.That(text.ToGeometries().Length, Is.EqualTo(8));
        FormattedText emoji = text.GetNonOutlineGlyph(4)!;
        FormattedText joinedEmoji = text.GetNonOutlineGlyph(5)!;
        using SKBitmap grouped = Draw(text, scale);
        using SKBitmap split = Draw(text, scale, split: true);
        Assert.Multiple(() =>
        {
            Assert.That(ColorPixels(split), Is.EqualTo(ColorPixels(grouped)));
            Assert.That(text.ActualBounds.Contains(emoji.ActualBounds), Is.True);
            Assert.That(text.ActualBounds.Contains(joinedEmoji.ActualBounds), Is.True);
            Assert.That(joinedEmoji.ActualBounds.Left, Is.GreaterThan(emoji.ActualBounds.Right));
        });

        if (scale == 1f)
        {
            string directory = ArtifactProvider.GetArtifactDirectory();
            using SKImage image = SKImage.FromBitmap(grouped);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            using Stream output = File.Create(Path.Combine(directory, "fallback.png"));
            data.SaveTo(output);
            TestContext.WriteLine(Path.GetFullPath(Path.Combine(directory, "fallback.png")));
        }
    }

    [Test]
    public void ChangingFallbacks_InvalidatesMeasuredAndScaledText()
    {
        FontManager.Instance.SetFallbackFonts([], null);
        using FormattedText text = CreateText("日本");
        SKTextBlob previous = text.GetTextBlob(2f)!;
        FontManager.Instance.SetFallbackFonts([s_uiFont], s_emojiFont);
        using FormattedText expected = CreateText("日本", s_uiFont);
        using SKBitmap actualImage = Draw(text);
        using SKBitmap expectedImage = Draw(expected);
        Assert.Multiple(() =>
        {
            Assert.That(text.GetTextBlob(2f), Is.Not.SameAs(previous));
            Assert.That(previous.Handle, Is.EqualTo(IntPtr.Zero));
            Assert.That(actualImage.Bytes, Is.EqualTo(expectedImage.Bytes));
        });
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void TextObject_RendersFallbackEmojiIdenticallyWhenSplit(float scale)
    {
        string? artifactDirectory = scale == 1f ? ArtifactProvider.GetArtifactDirectory() : null;
        Dictionary<int, SKColor> Render(bool split)
        {
            var block = new TextBlock
            {
                FontFamily = { CurrentValue = new FontFamily("Roboto") },
                Text = { CurrentValue = "AV 日本語 😀 👩‍💻\n123 © ©️ 1️⃣ 🇯🇵 👍🏽\n1\uFE0F #\uFE0F *\uFE0F 兀\uFE00" },
                Size = { CurrentValue = 48f },
                Spacing = { CurrentValue = 6f },
                Fill = { CurrentValue = Brushes.Black },
                SplitByCharacters = { CurrentValue = split },
            };
            using TextBlock.Resource resource = block.ToResource(CompositionContext.Default);
            using var node = new DrawableRenderNode(resource);
            using (var context = new GraphicsContext2D(node, new Size(800, 220)))
            {
                context.Clear(Colors.White);
                block.Render(context, resource);
            }
            using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                OutputScale = scale,
                TargetDomain = new Rect(0, 0, 800, 220),
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            });
            using RenderNodeRasterization raster = renderer.Rasterize();
            Assert.That(raster.IsEmpty, Is.False);
            if (!split && scale == 1f)
            {
                string file = Path.Combine(artifactDirectory!, "text-object.png");
                Assert.That(raster.Bitmap!.Save(file, EncodedImageFormat.Png), Is.True);
            }
            return ColorPixels(raster.Bitmap!.SKBitmap);
        }

        Dictionary<int, SKColor> grouped = Render(false);
        Dictionary<int, SKColor> split = Render(true);
        Assert.Multiple(() =>
        {
            Assert.That(grouped.Count, Is.GreaterThan(100));
            Assert.That(split, Is.EqualTo(grouped));
        });
    }

    private static FormattedText CreateText(string value, FontFamily? family = null) => new()
    {
        Font = family ?? new FontFamily("Roboto"),
        Text = value,
        Size = 64f,
    };

    [TestCase("size")]
    [TestCase("spacing")]
    [TestCase("text")]
    [TestCase("font")]
    [TestCase("weight")]
    [TestCase("style")]
    public void CachedFontSelection_AfterAPropertyChange_MatchesFreshRendering(string property)
    {
        using FormattedText text = CreateText("A日😀B本👩‍💻");
        _ = text.GetTextBlob(2f);
        switch (property)
        {
            case "size": text.Size = 48; break;
            case "spacing": text.Spacing = 9; break;
            case "text": text.Text = "©️C語😁"; break;
            case "font": text.Font = s_uiFont; break;
            case "weight": text.Weight = FontWeight.Bold; break;
            case "style": text.Style = FontStyle.Italic; break;
        }
        using var fresh = new FormattedText
        {
            Font = text.Font,
            Style = text.Style,
            Weight = text.Weight,
            Text = text.Text,
            Size = text.Size,
            Spacing = text.Spacing,
        };
        using SKBitmap actual = Draw(text, 0.75f);
        using SKBitmap expected = Draw(fresh, 0.75f);
        Assert.That(actual.Bytes, Is.EqualTo(expected.Bytes));
    }

    [Test]
    public void RightToLeftFallbacks_KeepVisualOrderAcrossDensitiesAndRemeasure()
    {
        using FormattedText text = CreateText("א😀😁");
        _ = text.GetTextBlob(2f);
        _ = text.GetTextBlob(0.5f);
        text.Size = 48;
        using FormattedText fresh = CreateText("א😀😁");
        fresh.Size = text.Size;
        using SKBitmap actual = Draw(text, 0.75f);
        using SKBitmap expected = Draw(fresh, 0.75f);
        Assert.That(actual.Bytes, Is.EqualTo(expected.Bytes));

        // Place each grapheme independently in its visual order, from left to right.
        using FormattedText leftEmoji = CreateText("😁", s_emojiFont);
        using FormattedText rightEmoji = CreateText("😀", s_emojiFont);
        using FormattedText hebrew = CreateText("א");
        using var surface = SKSurface.Create(new SKImageInfo(900, 300));
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.Translate(16 * 0.75f, 100 * 0.75f);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        float x = 0;
        foreach (FormattedText part in new[] { leftEmoji, rightEmoji, hebrew })
        {
            part.Size = text.Size;
            surface.Canvas.DrawText(part.GetTextBlob(0.75f), x, 0, paint);
            x += part.Bounds.Width * 0.75f;
        }
        using SKImage image = surface.Snapshot();
        using SKBitmap visualOrder = SKBitmap.FromImage(image);
        Assert.That(actual.Bytes, Is.EqualTo(visualOrder.Bytes));
    }

    private static SKBitmap Draw(FormattedText text, float scale = 1f, bool split = false)
    {
        using var surface = SKSurface.Create(new SKImageInfo(900, 300));
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.Translate(16 * scale, 100 * scale);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        if (split)
        {
            for (int i = 0; i < text.ToGeometries().Length; i++)
            {
                if (text.GetNonOutlineGlyph(i) is { } glyph)
                    surface.Canvas.DrawText(glyph.GetTextBlob(scale), 0, 0, paint);
            }
        }
        else
        {
            surface.Canvas.DrawText(text.GetTextBlob(scale), 0, 0, paint);
        }
        using SKImage image = surface.Snapshot();
        return SKBitmap.FromImage(image);
    }

    private static Dictionary<int, SKColor> ColorPixels(SKBitmap bitmap)
    {
        var pixels = new Dictionary<int, SKColor>();
        for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor pixel = bitmap.GetPixel(x, y);
                if (pixel.Alpha > 0 && (pixel.Red != pixel.Green || pixel.Green != pixel.Blue))
                    pixels.Add(y * bitmap.Width + x, pixel);
            }
        return pixels;
    }
}
