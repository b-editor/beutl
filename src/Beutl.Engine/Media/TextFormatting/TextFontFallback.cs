using System.Globalization;
using System.Text;
using SkiaSharp;

namespace Beutl.Media.TextFormatting;

internal static class TextFontFallback
{
    internal readonly record struct Run(SKTypeface Typeface, int Start, int Length);

    public static List<Run> GetRuns(ReadOnlySpan<char> text, SKFont primary, FontStyle style, FontWeight weight)
    {
        (FontFamily[] families, FontFamily? emoji) = FontManager.Instance.GetFallbackFamilies();
        if (families.Length == 0 && emoji is null)
            return text.IsEmpty ? [] : [new Run(primary.Typeface, 0, text.Length)];

        var fonts = new Dictionary<Typeface, SKFont?>();
        Dictionary<SKTypeface, TextShaper>? shapers = null;
        var runs = new List<Run>();
        try
        {
            for (int start = 0; start < text.Length;)
            {
                // A combining sequence, surrogate pair or ZWJ emoji must use one font as a unit.
                int length = StringInfo.GetNextTextElementLength(text[start..]);
                ReadOnlySpan<char> cluster = text.Slice(start, length);
                SKTypeface face = primary.Typeface;
                if (emoji is not null && EmojiPresentation.IsEmoji(cluster)
                    && GetFont(new Typeface(emoji)) is { } emojiFont && Covers(emojiFont, text, start, length))
                {
                    face = emojiFont.Typeface;
                }
                else if (!Covers(primary, text, start, length))
                {
                    foreach (FontFamily candidate in families)
                    {
                        if (GetFont(new Typeface(candidate, style, weight)) is { } fallbackFont
                            && Covers(fallbackFont, text, start, length))
                        {
                            face = fallbackFont.Typeface;
                            break;
                        }
                    }
                }

                // Keep adjacent text in the same font together, preserving kerning and ligatures.
                if (runs.Count > 0 && ReferenceEquals(runs[^1].Typeface, face))
                    runs[^1] = runs[^1] with { Length = runs[^1].Length + length };
                else
                    runs.Add(new Run(face, start, length));
                start += length;
            }
        }
        finally
        {
            foreach (SKFont? font in fonts.Values)
                font?.Dispose();
            if (shapers is not null)
            {
                foreach (TextShaper shaper in shapers.Values)
                    shaper.Dispose();
            }
        }

        return runs;

        bool Covers(SKFont font, ReadOnlySpan<char> content, int start, int length)
        {
            ReadOnlySpan<char> cluster = content.Slice(start, length);
            if (HasNominalGlyphs(font, cluster))
                return true;

            // A surrogate pair is one scalar, while a combining sequence can normalize to
            // a glyph the font has even when it lacks one of the individual cmap entries.
            int scalarCount = 0;
            foreach (Rune _ in cluster.EnumerateRunes())
            {
                if (++scalarCount > 1)
                    break;
            }
            if (scalarCount < 2)
                return false;

            shapers ??= [];
            if (!shapers.TryGetValue(font.Typeface, out TextShaper? shaper))
            {
                shaper = new TextShaper(font.Typeface);
                shapers.Add(font.Typeface, shaper);
            }
            using var buffer = new HarfBuzzSharp.Buffer();
            buffer.AddUtf16(content, start, length);
            buffer.GuessSegmentProperties();
            return shaper.HasGlyphs(buffer);
        }

        SKFont? GetFont(Typeface typeface)
        {
            if (!fonts.TryGetValue(typeface, out SKFont? font))
            {
                font = FontManager.Instance.TryResolveSkia(typeface, out SKTypeface face) ? new SKFont(face) : null;
                fonts.Add(typeface, font);
            }
            return font;
        }
    }

    private static bool HasNominalGlyphs(SKFont font, ReadOnlySpan<char> text)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            // These control shaping rather than needing an independently visible glyph.
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Format or UnicodeCategory.Control
                || rune.Value is >= 0xFE00 and <= 0xFE0F or >= 0xE0100 and <= 0xE01EF)
                continue;
            if (!font.ContainsGlyph(rune.Value))
                return false;
        }

        return true;
    }
}
