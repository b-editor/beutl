using Beutl.Logging;
using Beutl.Media;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Services;

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
    /// </summary>
    /// <param name="rowOffsets">
    /// Layer offsets, from the anchor layer, that the inserted clips occupy. A sparse selection
    /// (layers 0 and 2) is not blocked by content or a lock on a layer it never lands on.
    /// </param>
    internal static (MediaTime Start, int ZIndex) FindFreeAnchor(
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
                    "FindFreeAnchor gave up after {Steps} steps; using last candidate at start={Start}, zIndex={ZIndex}.",
                    searchSteps, newStart, newZIndex);
                return (newStart, newZIndex);
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
        foreach (Element child in scene.Children)
        {
            if (!rowOffsets.Contains(child.ZIndex.Value - anchorZIndex)) continue;
            TimeRange other = child.Range;
            if (other == range || other.Intersects(range) || other.Contains(range) || range.Contains(other))
            {
                return true;
            }
        }

        return false;
    }
}
