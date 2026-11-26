using Beutl.Graphics.Shapes;
using Beutl.Media;

namespace Beutl.Graphics;

internal readonly record struct TextPartTag(
    long OccurrenceId, int LayoutRevision, TextPartIndices Indices, TextPartCounts Counts, WritingMode WritingMode)
{
    public TextPartPositions? Positions { get; init; }

    public bool IncludeHiddenUnits { get; init; }

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

// The occurrence precedes the target at TargetIndex, or follows every target when TargetIndex equals the count.
internal readonly record struct ClippedTextOccurrence(int TargetIndex, TextPartTag Tag);
