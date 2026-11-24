using Beutl.Media;
using Beutl.Media.TextFormatting;

namespace Beutl.Graphics.Shapes;

internal readonly record struct TextPartIndices(int Character, int Word, int Line);

internal readonly record struct TextPartCounts(int Characters, int Words, int Lines);

internal sealed class TextPartLayout
{
    private readonly Dictionary<FormattedText, TextPartIndices[]> _runs;

    private TextPartLayout(
        Dictionary<FormattedText, TextPartIndices[]> runs, TextPartCounts counts, WritingMode writingMode, int revision)
    {
        _runs = runs;
        Counts = counts;
        WritingMode = writingMode;
        Revision = revision;
    }

    public TextPartCounts Counts { get; }

    public WritingMode WritingMode { get; }

    public int Revision { get; }

    public ReadOnlySpan<TextPartIndices> GetGlyphs(FormattedText run) => _runs[run];

    public static TextPartLayout Create(
        TextElements elements, VerticalTextLayout? verticalLayout, WritingMode writingMode, int revision)
    {
        var runs = new Dictionary<FormattedText, TextPartIndices[]>(ReferenceEqualityComparer.Instance);
        int characters = 0;
        int words = 0;
        int lines = 0;

        if (verticalLayout is null)
        {
            foreach (Span<FormattedText> line in elements.Lines)
            {
                int word = -1;
                foreach (FormattedText run in line)
                    AddRun(run, ref word);
                lines++;
            }
        }
        else
        {
            foreach (VerticalTextLayout.Column column in verticalLayout.Columns)
            {
                int word = -1;
                foreach (VerticalTextLayout.Run run in column.Runs)
                    AddRun(run.Text, ref word);
                lines++;
            }
        }

        return new TextPartLayout(runs, new TextPartCounts(characters, words, lines), writingMode, revision);

        void AddRun(FormattedText run, ref int word)
        {
            ReadOnlySpan<char> text = run.Text.AsSpan();
            ReadOnlySpan<FormattedText.GlyphPart> glyphs = run.GetGlyphParts();
            var hasInk = new bool[text.Length];
            foreach (FormattedText.GlyphPart glyph in glyphs)
                hasInk[glyph.Cluster] |= glyph.HasInk;

            // Shaping can emit clusters in visual order and emit several glyphs for one cluster.
            var sourceIndices = new TextPartIndices[text.Length];
            for (int offset = 0; offset < text.Length; offset++)
            {
                if (char.IsWhiteSpace(text[offset]))
                    word = -1;
                else if (word < 0)
                    word = words++;

                int character = word >= 0 && hasInk[offset] ? characters++ : -1;
                sourceIndices[offset] = new TextPartIndices(character, word, lines);
            }

            var glyphIndices = new TextPartIndices[glyphs.Length];
            for (int i = 0; i < glyphs.Length; i++)
                glyphIndices[i] = sourceIndices[glyphs[i].Cluster];
            runs.Add(run, glyphIndices);
        }
    }
}
