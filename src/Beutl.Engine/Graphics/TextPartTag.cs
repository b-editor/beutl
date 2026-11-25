using Beutl.Graphics.Shapes;
using Beutl.Media;

namespace Beutl.Graphics;

internal readonly record struct TextPartTag(
    long OccurrenceId, int LayoutRevision, TextPartIndices Indices, TextPartCounts Counts, WritingMode WritingMode)
{
    public static TextPartTag? GetCommon<T>(IEnumerable<T> values, Func<T, TextPartTag?> getTag)
    {
        using IEnumerator<T> enumerator = values.GetEnumerator();
        if (!enumerator.MoveNext() || getTag(enumerator.Current) is not { } first)
            return null;

        while (enumerator.MoveNext())
        {
            // An unselectable glyph has no character identity to match against another glyph.
            if (first.Indices.Character < 0 || getTag(enumerator.Current) != first)
                return null;
        }

        return first;
    }
}
