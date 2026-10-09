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
                SKFont? emojiFont = emoji is not null && EmojiPresentation.IsEmoji(cluster)
                    ? GetFont(new Typeface(emoji)) : null;
                bool emojiCovered = emojiFont is not null && Covers(emojiFont, text, start, length);
                bool requireJoinedGlyph = cluster.Contains('\u200D') && EmojiPresentation.IsEmoji(cluster);
                if (emojiCovered && (!requireJoinedGlyph || Covers(emojiFont!, text, start, length, true)))
                {
                    face = emojiFont!.Typeface;
                }
                else if (!Covers(primary, text, start, length, requireJoinedGlyph))
                {
                    SKTypeface? componentFace = emojiCovered ? emojiFont!.Typeface : null;
                    if (componentFace is null && requireJoinedGlyph && Covers(primary, text, start, length))
                        componentFace = primary.Typeface;
                    bool found = false;
                    foreach (FontFamily candidate in families)
                    {
                        if (GetFont(new Typeface(candidate, style, weight)) is not { } fallbackFont)
                            continue;
                        if (Covers(fallbackFont, text, start, length, requireJoinedGlyph))
                        {
                            face = fallbackFont.Typeface;
                            found = true;
                            break;
                        }
                        if (componentFace is null && requireJoinedGlyph && Covers(fallbackFont, text, start, length))
                            componentFace = fallbackFont.Typeface;
                    }
                    // If no font joins the sequence, display its component emoji instead of tofu.
                    if (!found && componentFace is not null)
                        face = componentFace;
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

        bool Covers(SKFont font, ReadOnlySpan<char> content, int start, int length, bool requireJoinedGlyph = false)
        {
            ReadOnlySpan<char> cluster = content.Slice(start, length);
            int scalarCount = 0;
            int previous = 0;
            bool hasNominalGlyphs = true;
            foreach (Rune rune in cluster.EnumerateRunes())
            {
                if (rune.Value is >= 0xFE00 and <= 0xFE0F or >= 0xE0100 and <= 0xE01EF)
                {
                    // VS16 is routed by EmojiPresentation. A VS16-only glyph mapping does not
                    // cover VS15; ordinary text fonts can use their nominal glyph without a map.
                    if (rune.Value == 0xFE0E && scalarCount > 0)
                    {
                        TextShaper shaper = GetShaper(font.Typeface);
                        if (!shaper.HasVariationGlyph(previous, 0xFE0E)
                            && shaper.HasVariationGlyph(previous, 0xFE0F))
                            return false;
                    }
                    // Other selectors require a cmap variation mapping; shaping alone can drop
                    // an unsupported selector and incorrectly claim the base glyph covers it.
                    if (rune.Value is not (0xFE0E or 0xFE0F) && scalarCount > 0
                        && !GetShaper(font.Typeface).HasVariationGlyph(previous, (uint)rune.Value))
                        return false;
                }
                else if (Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.Format or UnicodeCategory.Control)
                         && !font.ContainsGlyph(rune.Value))
                {
                    hasNominalGlyphs = false;
                }
                previous = rune.Value;
                scalarCount++;
            }
            if (hasNominalGlyphs && !requireJoinedGlyph)
                return true;

            // A surrogate pair is one scalar, while a combining sequence can normalize to
            // a glyph the font has even when it lacks one of the individual cmap entries.
            if (scalarCount < 2)
                return false;

            using var buffer = new HarfBuzzSharp.Buffer();
            buffer.AddUtf16(content, start, length);
            buffer.GuessSegmentProperties();
            return GetShaper(font.Typeface).HasGlyphs(buffer, requireJoinedGlyph);
        }

        TextShaper GetShaper(SKTypeface face)
        {
            shapers ??= [];
            if (!shapers.TryGetValue(face, out TextShaper? shaper))
            {
                shaper = new TextShaper(face);
                shapers.Add(face, shaper);
            }
            return shaper;
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

}
