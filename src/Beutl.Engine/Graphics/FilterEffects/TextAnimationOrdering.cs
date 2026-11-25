using Beutl.Media;

namespace Beutl.Graphics.Effects;

internal static class TextAnimationOrdering
{
    internal const float Unranked = -1;

    private readonly record struct Unit(int Occurrence, int Index, int Id, Rect? Bounds);

    internal static void ComputeUnitRanks(DelayAnimationOrder order, int seed, TextAnimationUnit unit,
        EffectTargets targets, Span<float> ranks)
    {
        var occurrences = new Dictionary<(long Id, int Revision), int>();
        var ids = new Dictionary<(int Occurrence, int Index), int>();
        var units = new List<Unit>(targets.Count);
        Span<int> targetUnits = targets.Count <= 128 ? stackalloc int[targets.Count] : new int[targets.Count];
        int nextOccurrence = 0;

        for (int i = 0; i < targets.Count; i++)
        {
            EffectTarget target = targets[i];
            if (target.TextPart is not { } text)
            {
                targetUnits[i] = units.Count;
                units.Add(new Unit(nextOccurrence++, 0, units.Count, target.IsEmpty ? null : target.Bounds));
                continue;
            }

            var source = (text.OccurrenceId, text.LayoutRevision);
            if (!occurrences.TryGetValue(source, out int occurrence))
            {
                occurrence = nextOccurrence++;
                occurrences.Add(source, occurrence);
            }

            if (text.Indices.Character < 0)
            {
                targetUnits[i] = -1;
                continue;
            }

            int index = GetUnitIndex(unit, order, text);
            var key = (occurrence, index);
            Rect? bounds = target.IsEmpty ? null : target.CompleteBounds ?? target.Bounds;
            if (ids.TryGetValue(key, out int id))
            {
                if (bounds is { } part)
                {
                    Unit entry = units[id];
                    units[id] = entry with { Bounds = entry.Bounds?.Union(part) ?? part };
                }
            }
            else
            {
                id = units.Count;
                ids.Add(key, id);
                units.Add(new Unit(occurrence, index, id, bounds));
            }

            targetUnits[i] = id;
        }

        // Rank in logical order within each occurrence, then scatter back to paint order.
        units.Sort(static (a, b) => a.Occurrence == b.Occurrence
            ? a.Index.CompareTo(b.Index)
            : a.Occurrence.CompareTo(b.Occurrence));
        var unitBounds = new Rect?[units.Count];
        Span<float> unitRanks = units.Count <= 128 ? stackalloc float[units.Count] : new float[units.Count];
        Span<float> byId = units.Count <= 128 ? stackalloc float[units.Count] : new float[units.Count];
        for (int i = 0; i < units.Count; i++)
            unitBounds[i] = units[i].Bounds;
        DelayAnimationOrdering.ComputeRanks(order, seed, unitBounds, unitRanks);
        for (int i = 0; i < units.Count; i++)
            byId[units[i].Id] = unitRanks[i];
        for (int i = 0; i < ranks.Length; i++)
            ranks[i] = targetUnits[i] < 0 ? Unranked : byId[targetUnits[i]];
    }

    private static int GetUnitIndex(TextAnimationUnit unit, DelayAnimationOrder order, TextPartTag part)
    {
        bool acrossLines = part.WritingMode == WritingMode.VerticalRl
            ? order is DelayAnimationOrder.LeftToRight or DelayAnimationOrder.RightToLeft
            : order is DelayAnimationOrder.TopToBottom or DelayAnimationOrder.BottomToTop;
        if (acrossLines)
            return part.Indices.Line;

        return unit switch
        {
            TextAnimationUnit.Words => part.Indices.Word,
            TextAnimationUnit.Lines => part.Indices.Line,
            _ => part.Indices.Character,
        };
    }
}
