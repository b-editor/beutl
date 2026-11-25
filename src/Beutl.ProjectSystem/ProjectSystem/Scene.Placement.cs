using Beutl.Configuration;
using Beutl.Media;

namespace Beutl.ProjectSystem;

public partial class Scene
{
    // The layer above every clip in the scene.
    internal int GetFirstFreeLayer()
        => Children.Select(item => item.ZIndex.Value).DefaultIfEmpty(-1).Max() + 1;

    // The layer reached by counting up from first, leaving out locked layers.
    internal int CountUnlockedLayers(int first, int count)
    {
        int layer = first;
        for (int remaining = Math.Max(count, 0); ; layer++)
        {
            if (IsLayerLocked(layer)) continue;
            if (remaining-- == 0) return layer;
        }
    }

    private int NearestLayerNumber(Element element)
    {
        if (IsOverlapping(element.Range, element.ZIndex.Value))
        {
            int layerMax = Children.Max(i => i.ZIndex.Value);
            SceneElementIndex index = ElementIndex;

            // 使うことができるレイヤー番号。ロックされたレイヤーには自動配置しない。
            var numbers = new List<int>();

            for (int l = 0; l <= layerMax; l++)
            {
                if (!IsLayerLocked(l) && !index.Intersects(l, element.Range))
                {
                    numbers.Add(l);
                }
            }

            if (numbers.Count < 1)
            {
                int next = layerMax + 1;
                while (IsLayerLocked(next)) next++;
                return next;
            }

            return numbers.Nearest(element.ZIndex.Value);
        }

        return element.ZIndex.Value;
    }

    internal (Element? Before, Element? After, Element? Cover) GetBeforeAndAfterAndCover(Element element)
    {
        var (before, after, cover) = GetPlacementNeighbors(this, element, element.ZIndex.Value, element.Range);
        return (before?.Element, after?.Element, cover);
    }

    private readonly record struct PlacementNeighbor(Element Element, TimeRange Range);

    private static (PlacementNeighbor? Before, PlacementNeighbor? After, Element? Cover) GetPlacementNeighbors(
        Scene? scene, Element element, int layer, TimeRange range,
        IReadOnlyDictionary<Element, (TimeRange Range, int Layer)>? priorPlacements = null)
    {
        if (scene == null) return default;

        PlacementNeighbor? before = null;
        PlacementNeighbor? after = null;
        Element? cover = null;
        foreach (Element item in scene.Children.AsSpan())
        {
            if (item == element) continue;

            (TimeRange other, int otherLayer) = priorPlacements != null && priorPlacements.TryGetValue(item, out var placed)
                ? placed
                : (item.Range, item.ZIndex.Value);
            if (otherLayer != layer) continue;

            if (other.Start < range.Start && (before == null || before.Value.Range.Start <= other.Start))
                before = new PlacementNeighbor(item, other);
            if (other.End > range.End && (after == null || after.Value.Range.End >= other.End))
                after = new PlacementNeighbor(item, other);
            if (range.Contains(other) || range == other)
                cover = item;
        }

        return (before, after, cover);
    }

    private bool IsOverlapping(TimeRange timeRange, int zindex)
    {
        return ElementIndex.Overlaps(zindex, timeRange);
    }

    private bool IsFreeRange(TimeRange range, int zindex)
    {
        return range.Duration > MediaTime.Zero && !IsOverlapping(range, zindex);
    }

    private (TimeRange Range, int ZIndex) GetCorrectPosition(Element element, ElementOverlapHandling handling)
    {
        bool overlapping = IsOverlapping(element.Range, element.ZIndex.Value);

        if (!overlapping || handling.HasFlag(ElementOverlapHandling.Allow))
            return (element.Range, element.ZIndex.Value);

        if (handling == ElementOverlapHandling.ThrowException)
            throw new InvalidOperationException("要素の位置が無効です");

        (Element? before, Element? after, Element? cover) = GetBeforeAndAfterAndCover(element);
        var candidateStart = new List<MediaTime>(2);
        var candidateEnd = new List<MediaTime>(2);
        if (cover != null)
        {
            candidateEnd.Add(cover.Start.Value);
            candidateStart.Add(cover.Range.End);
        }

        if (after != null) candidateEnd.Add(after.Start.Value);
        if (before != null) candidateStart.Add(before.Range.End);

        MediaTime start = element.Start.Value;
        MediaTime end = element.Range.End;

        if (handling.HasFlag(ElementOverlapHandling.Start) && handling.HasFlag(ElementOverlapHandling.Length))
        {
            foreach (MediaTime cEnd in candidateEnd)
            {
                TimeRange range = TimeRange.FromRange(start, cEnd);
                if (IsFreeRange(range, element.ZIndex.Value))
                {
                    return (range, element.ZIndex.Value);
                }

                foreach (MediaTime cStart in candidateStart)
                {
                    range = TimeRange.FromRange(cStart, cEnd);
                    if (IsFreeRange(range, element.ZIndex.Value))
                    {
                        return (range, element.ZIndex.Value);
                    }
                }
            }
        }

        if (handling.HasFlag(ElementOverlapHandling.Length))
        {
            foreach (MediaTime item in candidateEnd)
            {
                TimeRange range = TimeRange.FromRange(start, item);
                if (IsFreeRange(range, element.ZIndex.Value))
                {
                    return (range, element.ZIndex.Value);
                }
            }
        }

        if (handling.HasFlag(ElementOverlapHandling.Start))
        {
            foreach (MediaTime item in candidateStart)
            {
                TimeRange range = TimeRange.FromRange(item, end);
                if (IsFreeRange(range, element.ZIndex.Value))
                {
                    return (range, element.ZIndex.Value);
                }
            }
        }

        return (element.Range, NearestLayerNumber(element));
    }

    // Grows the scene to reach end, when the setting that adjusts its duration is on.
    internal void ExtendDurationToCover(MediaTime end, bool autoAdjustDuration)
    {
        if (autoAdjustDuration && Duration.Value + Start.Value < end)
        {
            Duration.Value = end - Start.Value;
        }
    }

    private void AddChildCore(Element element, ElementOverlapHandling overlapHandling, bool autoAdjustDuration)
    {
        (TimeRange range, int zIndex) = GetCorrectPosition(element, overlapHandling);
        element.Start.Value = range.Start;
        element.Length.Value = range.Duration;
        element.ZIndex.Value = zIndex;
        Children.Add(element);

        ExtendDurationToCover(range.End, autoAdjustDuration);
    }

    private void MoveChildCore(int zIndex, MediaTime newStart, MediaTime newLength, Element element, bool autoAdjustDuration)
    {
        if (ResolveChildMoveStart(zIndex, newStart, newLength, element) is { } start)
        {
            element.Start.Value = start;
            element.Length.Value = newLength;
            element.ZIndex.Value = zIndex;
        }

        ExtendDurationToCover(element.Range.End, autoAdjustDuration);
    }

    internal static MediaTime? ResolveChildMoveStart(int zIndex, MediaTime newStart, MediaTime newLength, Element element,
        IReadOnlyDictionary<Element, (TimeRange Range, int Layer)>? priorPlacements = null)
    {
        MediaTime newEnd = newStart + newLength;
        var (before, after, cover) = GetPlacementNeighbors(element.HierarchicalParent as Scene, element, zIndex,
            TimeRange.FromRange(newStart, newEnd), priorPlacements);

        if (before is { Range.End: var beforeEnd } && beforeEnd >= newStart)
        {
            return after == null || after.Value.Range.Start - beforeEnd >= newLength ? beforeEnd : null;
        }

        if (after is { Range.Start: var afterStart } && afterStart < newEnd)
        {
            MediaTime start = afterStart - newLength;
            return start >= MediaTime.Zero && (before == null || afterStart - before.Value.Range.End >= newLength)
                ? start
                : null;
        }

        return cover == null ? newStart : null;
    }

    // Returns whether the elements moved. A drag fits around what it hits, which can move them further than asked;
    // without fitAroundConflicts they move by exactly deltaTime or not at all.
    private bool MoveChildrenCore(Element[] elements, int deltaZIndex, MediaTime deltaTime, bool fitAroundConflicts, bool autoAdjustDuration)
    {
        if (ResolveGroupMove(elements, deltaZIndex, deltaTime, fitAroundConflicts) is not { } resolvedDelta)
            return false;

        MediaTime newSceneDuration = Duration.Value;
        if (autoAdjustDuration)
        {
            MediaTime maxEndingTime = elements.Max(i => i.Range.End + resolvedDelta);
            if (newSceneDuration + Start.Value < maxEndingTime)
            {
                newSceneDuration = maxEndingTime - Start.Value;
            }
        }

        // A matte follows a source moved with it. Any other layer it names still holds its source.
        List<(Element Matte, Element Source)> movedMattes = deltaZIndex != 0 ? FindMatteSourcesAmong(elements) : [];
        foreach (Element item in elements)
        {
            item.Start.Value += resolvedDelta;
            item.ZIndex.Value += deltaZIndex;
        }

        foreach ((Element matte, Element source) in movedMattes)
        {
            matte.MatteSourceZIndex.Value = source.ZIndex.Value;
        }

        if (autoAdjustDuration)
        {
            Duration.Value = newSceneDuration;
        }

        return true;
    }

    internal MediaTime? ResolveGroupMove(Element[] elements, int deltaZIndex, MediaTime deltaTime, bool fitAroundConflicts = true)
    {
        var selected = elements.ToHashSet();
        SceneElementIndex index = ElementIndex;
        bool conflict = HasConflict(elements, index, selected, deltaZIndex, deltaTime);
        foreach (Element item in elements)
        {
            if (!conflict || !fitAroundConflicts)
            {
                break;
            }

            MediaTime? newDeltaStart = DeltaStart(item, index, selected, deltaZIndex, deltaTime);
            if (newDeltaStart.HasValue && newDeltaStart.Value != deltaTime)
            {
                deltaTime = newDeltaStart.Value;
                conflict = HasConflict(elements, index, selected, deltaZIndex, deltaTime);
            }
        }

        return conflict ? null : deltaTime;
    }

    // The elements whose matte names a layer where another of them is at the same time, and that other element.
    internal static List<(Element Matte, Element Source)> FindMatteSourcesAmong(IReadOnlyCollection<Element> elements)
    {
        var result = new List<(Element Matte, Element Source)>();
        foreach (Element element in elements)
        {
            if (element.MatteSourceKind.Value == MatteSourceKind.Layer
                && element.MatteSourceZIndex.Value is { } layer
                && elements.FirstOrDefault(other => other != element
                    && other.ZIndex.Value == layer
                    && other.Range.Intersects(element.Range)) is { } source)
            {
                result.Add((element, source));
            }
        }

        return result;
    }

    private static bool HasConflict(Element[] elements, SceneElementIndex index, IReadOnlySet<Element> selected,
        int deltaZIndex, MediaTime deltaTime)
    {
        foreach (Element item in elements)
        {
            TimeRange newRange = item.Range.AddStart(deltaTime);
            int newLayer = item.ZIndex.Value + deltaZIndex;
            if (newLayer < 0 || newRange.Start < MediaTime.Zero)
                return true;

            if (index.Intersects(newLayer, newRange, selected)) return true;
        }

        return false;
    }

    private static MediaTime? DeltaStart(Element element, SceneElementIndex index, IReadOnlySet<Element> selected,
        int deltaZIndex, MediaTime deltaTime)
    {
        MediaTime newStart = element.Start.Value + deltaTime;

        MediaTime newEnd = newStart + element.Length.Value;
        int newIndex = element.ZIndex.Value + deltaZIndex;
        (Element? before, Element? after) = index.Neighbors(newIndex, new TimeRange(newStart, element.Length.Value), selected);

        if (before != null && before.Range.End >= newStart)
        {
            if ((after != null && (after.Start.Value - before.Range.End) >= element.Length.Value) || after == null)
            {
                return before.Range.End - element.Start.Value;
            }
        }
        else if (after != null && after.Start.Value < newEnd)
        {
            MediaTime ns = after.Start.Value - element.Length.Value;
            if (((before != null && (after.Start.Value - before.Range.End) >= element.Length.Value) || before == null) &&
                ns >= MediaTime.Zero)
            {
                return ns - element.Start.Value;
            }
        }
        else if (newStart < MediaTime.Zero)
        {
            return -element.Start.Value;
        }

        return null;
    }
}
