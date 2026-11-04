using Beutl.Logging;
using Beutl.Media;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Services;

/// <summary>
/// The free place found for elements kept together, with the range, lowest layer and layer offsets they had when
/// it was found.
/// </summary>
internal readonly record struct GroupPlacement(
    TimeRange Range, int MinZIndex, IReadOnlySet<int> RowOffsets, MediaTime Start, int ZIndex)
{
    public MediaTime StartDelta => Start - Range.Start;

    public int LayerDelta => ZIndex - MinZIndex;
}

/// <summary>
/// Finds the anchor slot for clips inserted at a clicked position. Duplicate and paste share this
/// search so an occupied slot sends the clips to a free one instead of silently shortening them.
/// </summary>
internal static class ElementPlacementHelper
{
    private const int MaxSearchSteps = 100_000;

    private static readonly ILogger s_logger = Log.CreateLogger(typeof(ElementPlacementHelper));

    /// <summary>
    /// Spiral search around the clicked position for a non-overlapping slot,
    /// bounded by <see cref="MaxSearchSteps"/> so a packed timeline can't hang.
    /// Returns <see langword="null"/> when no free slot was found within the limit.
    /// </summary>
    /// <param name="rowOffsets">
    /// Layer offsets, from the anchor layer, that the inserted clips occupy. A sparse selection
    /// (layers 0 and 2) is not blocked by content or a lock on a layer it never lands on.
    /// </param>
    internal static (MediaTime Start, int ZIndex)? FindFreeAnchor(
        Scene scene,
        TimeRange range,
        IReadOnlySet<int> rowOffsets,
        IReadOnlySet<int> lockedRows,
        MediaTime clickedFrame,
        int clickedLayer)
    {
        MediaTime step = SceneTimeRangeService.GetFrameDuration(scene);
        MediaTime length = range.Duration;

        MediaTime newStart = clickedFrame;
        int newZIndex = clickedLayer;

        if (IsPlacementFree(scene, new TimeRange(newStart, length), newZIndex, rowOffsets, lockedRows))
        {
            return (newStart, newZIndex);
        }

        // 時計回り: 右→下→左→上
        int[] dx = [1, 0, -1, 0];
        int[] dz = [0, 1, 0, -1];

        int dir = 0;
        int stepLen = 1;
        int stepped = 0;
        int turnCount = 0;
        int searchSteps = 0;

        while (true)
        {
            newStart += new MediaTime(step.Flicks * dx[dir]);
            newZIndex += dz[dir];

            if (newStart < MediaTime.Zero) newStart = MediaTime.Zero;
            if (newZIndex < 0) newZIndex = 0;

            if (IsPlacementFree(scene, new TimeRange(newStart, length), newZIndex, rowOffsets, lockedRows))
            {
                return (newStart, newZIndex);
            }

            if (++searchSteps >= MaxSearchSteps)
            {
                s_logger.LogWarning(
                    "FindFreeAnchor gave up after {Steps} steps without finding a free slot.",
                    searchSteps);
                return null;
            }

            stepped++;
            if (stepped == stepLen)
            {
                stepped = 0;
                dir = (dir + 1) & 3;
                turnCount++;
                if ((turnCount & 1) == 0)
                    stepLen++;
            }
        }
    }

    /// <summary>
    /// Searches for a free place for <paramref name="elements"/> kept together, around <paramref name="start"/> and
    /// <paramref name="layer"/>, or around where they are when those are left out.
    /// Returns <see langword="null"/> when no free slot was found within the limit.
    /// </summary>
    internal static GroupPlacement? FindFreePlacement(
        Scene scene, IReadOnlyList<Element> elements, MediaTime? start = null, int? layer = null)
    {
        (TimeRange range, int minZIndex, _) = DuplicateHelper.ComputePlacementRange(elements);
        // Only rows an element actually lands on matter: a sparse selection (layers 0 and 2) must not be blocked by
        // content or a lock on layer 1.
        HashSet<int> rowOffsets = elements.Select(element => element.ZIndex.Value - minZIndex).ToHashSet();
        // Snapshot once: the bounded spiral search probes these per candidate.
        HashSet<int> lockedRows = GetLockedRows(scene);
        if (FindFreeAnchor(scene, range, rowOffsets, lockedRows, start ?? range.Start, layer ?? minZIndex)
            is not { } anchor)
        {
            return null;
        }

        return new GroupPlacement(range, minZIndex, rowOffsets, anchor.Start, anchor.ZIndex);
    }

    /// <summary>The place for <paramref name="elements"/> kept together at exactly the given start and layer.</summary>
    internal static GroupPlacement PlaceAt(IReadOnlyList<Element> elements, MediaTime start, int layer)
    {
        (TimeRange range, int minZIndex, _) = DuplicateHelper.ComputePlacementRange(elements);
        return new GroupPlacement(
            range,
            minZIndex,
            elements.Select(element => element.ZIndex.Value - minZIndex).ToHashSet(),
            start,
            layer);
    }

    internal static HashSet<int> GetLockedRows(Scene scene)
        => scene.Layers.Where(layer => layer.IsLocked.Value).Select(layer => layer.ZIndex.Value).ToHashSet();

    /// <summary>
    /// Moves every element by the deltas. A matte that names its source layer moves with the elements, so it keeps
    /// the source it had among them, or its distance from them when they do not hold that layer.
    /// </summary>
    internal static void MoveTogether(IEnumerable<Element> elements, MediaTime startDelta, int layerDelta)
    {
        foreach (Element element in elements)
        {
            element.Start.Value += startDelta;
            element.ZIndex.Value += layerDelta;
            if (element.MatteSourceKind.Value == MatteSourceKind.Layer
                && element.MatteSourceZIndex.Value is { } source)
            {
                element.MatteSourceZIndex.Value = source + layerDelta;
            }
        }
    }

    internal static bool AnyDestinationRowLocked(
        int anchorZIndex, IReadOnlySet<int> rowOffsets, IReadOnlySet<int> lockedRows)
    {
        foreach (int offset in rowOffsets)
        {
            if (lockedRows.Contains(anchorZIndex + offset)) return true;
        }

        return false;
    }

    private static bool IsPlacementFree(
        Scene scene, TimeRange range, int anchorZIndex, IReadOnlySet<int> rowOffsets, IReadOnlySet<int> lockedRows)
        => !IsOverlapping(scene, range, anchorZIndex, rowOffsets)
           && !AnyDestinationRowLocked(anchorZIndex, rowOffsets, lockedRows);

    private static bool IsOverlapping(
        Scene scene, TimeRange range, int anchorZIndex, IReadOnlySet<int> rowOffsets)
    {
        SceneElementIndex index = scene.ElementIndex;
        foreach (int offset in rowOffsets)
        {
            if (index.Overlaps(anchorZIndex + offset, range)) return true;
        }

        return false;
    }
}
