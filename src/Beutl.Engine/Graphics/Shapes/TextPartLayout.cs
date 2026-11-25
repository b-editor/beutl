using Beutl.Media;
using Beutl.Media.TextFormatting;

namespace Beutl.Graphics.Shapes;

internal readonly record struct TextPartIndices(int Character, int Word, int Line);

internal readonly record struct TextPartCounts(int Characters, int Words, int Lines);

internal readonly record struct TextPartPlacement(
    TextPartIndices Indices, Point Anchor, float Distance, float LineLength, float LineOffset, bool Sideways);

// Retained by render fragments. It must not hold the disposable runs or their glyph geometry.
internal sealed class TextPartPositions(TextPartPlacement[] characters, TextAlignment alignment)
{
    public ReadOnlySpan<TextPartPlacement> Characters => characters;
    public TextAlignment Alignment { get; } = alignment;
}

internal sealed class TextPartLayout
{
    internal readonly record struct Run(FormattedText Text, Matrix Transform, TextPartIndices[] Glyphs);

    private TextPartLayout(
        List<Run> runs, TextPartPositions positions, TextPartCounts counts, WritingMode writingMode, int revision)
    {
        Runs = runs;
        Positions = positions;
        Counts = counts;
        WritingMode = writingMode;
        Revision = revision;
    }

    public IReadOnlyList<Run> Runs { get; }

    public TextPartPositions Positions { get; }

    public TextPartCounts Counts { get; }

    public WritingMode WritingMode { get; }

    public int Revision { get; }

    public TextPartTag CreateTag(long occurrence, TextPartIndices indices)
        => new(occurrence, Revision, indices, Counts, WritingMode) { Positions = Positions };

    public static TextPartLayout Create(
        TextElements elements, VerticalTextLayout? verticalLayout, WritingMode writingMode,
        TextAlignment alignment, float width, int revision)
    {
        var runs = new List<Run>();
        var characters = new List<TextPartPlacement>();
        int words = 0;
        int lines = 0;

        if (verticalLayout is null)
        {
            float bottom = 0;
            float firstBaseline = 0;
            foreach (Span<FormattedText> line in elements.Lines)
            {
                Size lineSize = TextBlock.MeasureLine(line);
                float baseline = bottom - TextBlock.MinAscent(line);
                if (lines == 0)
                    firstBaseline = baseline;
                float alignmentOffset = TextBlock.GetLineOffset(alignment, width, lineSize.Width);
                float right = alignmentOffset;
                float advance = 0;
                int word = -1;
                foreach (FormattedText run in line)
                {
                    if (run.Text.Length == 0)
                        continue;

                    float spacing = run.Spacing / 2;
                    Matrix transform = Matrix.CreateTranslation(right + spacing, baseline);
                    AddRun(run, transform, advance + spacing, lineSize.Width, baseline - firstBaseline, ref word);
                    float runWidth = run.Bounds.Width + run.Spacing;
                    right += runWidth;
                    advance += runWidth;
                }
                bottom += lineSize.Height;
                lines++;
            }
        }
        else
        {
            float right = verticalLayout.Size.Width;
            float firstCenter = 0;
            foreach (VerticalTextLayout.Column column in verticalLayout.Columns)
            {
                float center = right - column.Width / 2;
                if (lines == 0)
                    firstCenter = center;
                float top = TextBlock.GetLineOffset(alignment, verticalLayout.Size.Height, column.Height);
                int word = -1;
                foreach (VerticalTextLayout.Run run in column.Runs)
                {
                    Matrix transform = VerticalTextLayout.GetTransform(run, center, top);
                    AddRun(run.Text, transform, run.Offset, column.Height, firstCenter - center, ref word);
                }
                right -= column.Width;
                lines++;
            }
        }

        return new TextPartLayout(runs, new TextPartPositions(characters.ToArray(), alignment),
            new TextPartCounts(characters.Count, words, lines), writingMode, revision);

        void AddRun(FormattedText run, Matrix transform, float advance, float lineLength, float lineOffset, ref int word)
        {
            ReadOnlySpan<char> text = run.Text.AsSpan();
            ReadOnlySpan<FormattedText.GlyphPart> glyphs = run.GetGlyphParts();
            var clusters = new (bool HasGlyph, bool HasInk, float Start, float End)[text.Length];
            foreach (FormattedText.GlyphPart glyph in glyphs)
            {
                ref var cluster = ref clusters[glyph.Cluster];
                float start = MathF.Min(glyph.AdvanceStart, glyph.AdvanceEnd);
                float end = MathF.Max(glyph.AdvanceStart, glyph.AdvanceEnd);
                cluster = cluster.HasGlyph
                    ? (true, cluster.HasInk || glyph.HasInk, MathF.Min(cluster.Start, start), MathF.Max(cluster.End, end))
                    : (true, glyph.HasInk, start, end);
            }

            // Shaping can emit clusters in visual order and emit several glyphs for one cluster.
            var sourceIndices = new TextPartIndices[text.Length];
            for (int offset = 0; offset < text.Length; offset++)
            {
                if (char.IsWhiteSpace(text[offset]))
                    word = -1;
                else if (word < 0)
                    word = words++;

                var cluster = clusters[offset];
                int character = word >= 0 && cluster.HasInk ? characters.Count : -1;
                sourceIndices[offset] = new TextPartIndices(character, word, lines);
                if (character >= 0)
                {
                    float middle = cluster.Start + (cluster.End - cluster.Start) / 2;
                    Point anchor = run.Orientation == TextRunOrientation.Upright ? new(0, middle) : new(middle, 0);
                    characters.Add(new TextPartPlacement(sourceIndices[offset], anchor * transform,
                        advance + middle, lineLength, lineOffset, run.Orientation == TextRunOrientation.Sideways));
                }
            }

            var glyphIndices = new TextPartIndices[glyphs.Length];
            for (int i = 0; i < glyphs.Length; i++)
                glyphIndices[i] = sourceIndices[glyphs[i].Cluster];
            runs.Add(new Run(run, transform, glyphIndices));
        }
    }
}
