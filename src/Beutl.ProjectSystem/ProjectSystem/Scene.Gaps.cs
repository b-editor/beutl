using Beutl.Media;

namespace Beutl.ProjectSystem;

public partial class Scene
{
    /// <summary>
    /// Enumerates the gaps between adjacent elements on each ZIndex, ordered by
    /// ZIndex (ascending) then by gap start (ascending). A gap is the empty
    /// interval between one element's <see cref="Element.Range.End"/> and the
    /// next element's <see cref="Element.Start"/> on the same ZIndex when the
    /// next element starts strictly after the previous one ends. Overlapping
    /// or touching elements produce no gap, and the space before the first
    /// element on a ZIndex is not a gap.
    /// </summary>
    public IEnumerable<SceneGap> EnumerateGaps()
    {
        foreach (IGrouping<int, Element> zGroup in Children.GroupBy(e => e.ZIndex).OrderBy(g => g.Key))
        {
            List<Element> sorted = zGroup.OrderBy(e => e.Start).ThenBy(e => e.Range.End).ToList();
            if (sorted.Count == 0) continue;

            // The anchor is the run's furthest-ending element, so it ends exactly at the gap start.
            Element coveredEndElement = sorted[0];
            TimeSpan coveredEnd = coveredEndElement.Range.End;
            for (int i = 1; i < sorted.Count; i++)
            {
                Element next = sorted[i];
                if (next.Start > coveredEnd)
                {
                    yield return new SceneGap(zGroup.Key, new TimeRange(coveredEnd, next.Start - coveredEnd), coveredEndElement);
                }

                if (next.Range.End > coveredEnd)
                {
                    coveredEnd = next.Range.End;
                    coveredEndElement = next;
                }
            }
        }
    }

    /// <summary>
    /// Closes the first gap after the continuous-coverage run containing
    /// <paramref name="anchor"/> on <paramref name="anchor"/>'s ZIndex by
    /// shifting the subsequent unlocked elements on that ZIndex left by the gap
    /// size. An overlapping peer that extends the run past the anchor moves the
    /// target gap to the run's covered end. Returns <see langword="false"/> when
    /// there is no gap after the run (no next element, or the next element
    /// touches or overlaps the run), when the layer is locked, or when a locked
    /// element blocks every shiftable follower. Locked layers and locked
    /// elements are never moved; a locked element acts as an immovable barrier
    /// that halts the shift. Does not commit history; the caller owns the single
    /// <c>HistoryManager.Commit</c> boundary.
    /// </summary>
    public bool CloseGapAfter(Element anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        if (anchor.HierarchicalParent is not Scene scene || !ReferenceEquals(scene, this))
            return false;

        int z = anchor.ZIndex;
        // A locked layer's elements must not move, matching the other timeline mutation services.
        if (IsLayerLocked(z)) return false;

        List<Element> sorted = Children
            .Where(e => e.ZIndex == z)
            .OrderBy(e => e.Start)
            .ThenBy(e => e.Range.End)
            .ToList();
        if (sorted.Count == 0) return false;

        // The gap to close is the first empty interval after the continuous coverage run that
        // contains the anchor, so an earlier element covering past the anchor means no gap exists.
        TimeSpan coveredEnd = sorted[0].Range.End;
        bool anchorInRun = ReferenceEquals(sorted[0], anchor);
        for (int i = 1; i < sorted.Count; i++)
        {
            Element cur = sorted[i];
            if (cur.Start > coveredEnd)
            {
                if (anchorInRun)
                {
                    Element[] toShift = ShiftableAfter(z, cur.Start);
                    return toShift.Length != 0 && MoveChildrenAndDetectChange(toShift, coveredEnd - cur.Start);
                }

                coveredEnd = cur.Range.End;
                anchorInRun = ReferenceEquals(cur, anchor);
            }
            else
            {
                if (cur.Range.End > coveredEnd) coveredEnd = cur.Range.End;
                if (ReferenceEquals(cur, anchor)) anchorInRun = true;
            }
        }

        return false;
    }

    /// <summary>
    /// Closes every gap between elements on every ZIndex (the space before the
    /// first element on a ZIndex is not closed). Returns the number of gaps
    /// closed. Gaps are closed right-to-left within each ZIndex so earlier
    /// closes do not shift elements that later closes depend on. Locked layers
    /// are skipped and locked elements are never moved; a locked element acts as
    /// an immovable barrier that halts the shift. Does not commit history; the
    /// caller owns the single commit boundary.
    /// </summary>
    public int CloseAllGaps()
    {
        List<SceneGap> gaps = EnumerateGaps().ToList();
        if (gaps.Count == 0) return 0;

        int closed = 0;
        foreach (IGrouping<int, SceneGap> zGroup in gaps.GroupBy(g => g.ZIndex))
        {
            if (IsLayerLocked(zGroup.Key)) continue;

            foreach (SceneGap gap in zGroup.OrderByDescending(g => g.Range.Start))
            {
                TimeSpan delta = -gap.Range.Duration;
                if (delta == TimeSpan.Zero) continue;

                Element[] toShift = ShiftableAfter(zGroup.Key, gap.Range.End);
                if (toShift.Length == 0) continue;

                if (MoveChildrenAndDetectChange(toShift, delta))
                {
                    closed++;
                }
            }
        }

        return closed;
    }

    // The elements at or after fromStart that a gap close may slide left, in timeline order. A locked
    // element is a hard wall: iteration stops at the first locked start-group, so nothing at or beyond
    // it shifts across the lock (elements before it only move further left, never onto a lock).
    // Grouping by Start keeps same-start elements atomic, independent of Children enumeration order.
    private Element[] ShiftableAfter(int zIndex, TimeSpan fromStart)
    {
        if (IsLayerLocked(zIndex)) return [];

        var result = new List<Element>();
        foreach (IGrouping<TimeSpan, Element> startGroup in Children
            .Where(e => e.ZIndex == zIndex && e.Start >= fromStart)
            .GroupBy(e => e.Start)
            .OrderBy(g => g.Key))
        {
            if (startGroup.Any(e => e.IsLocked)) break;

            result.AddRange(startGroup);
        }

        return [.. result];
    }

    private bool MoveChildrenAndDetectChange(Element[] elements, TimeSpan deltaStart)
    {
        TimeSpan[] originalStarts = elements.Select(e => e.Start).ToArray();

        MoveChildren(0, deltaStart, elements);

        for (int i = 0; i < elements.Length; i++)
        {
            if (elements[i].Start != originalStarts[i])
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns the first gap (across all ZIndexes) that starts at or after
    /// <paramref name="currentTime"/>, or <see langword="null"/> when no such
    /// gap exists. A playhead sitting inside a gap starts past that gap, so it is
    /// skipped, but one resting exactly on a gap's start still finds it.
    /// </summary>
    /// <param name="searchRange">
    /// When set, a gap that does not intersect this range is dropped, and the filter/ordering use the
    /// gap's intersection with the range, so a gap that merely straddles the range (its ends lie beyond
    /// a shortened or offset scene) is still reachable through its visible portion. The returned gap is
    /// the raw gap, unclamped — the caller decides how to clamp it for display.
    /// </param>
    public SceneGap? FindNextGap(TimeSpan currentTime, TimeRange? searchRange = null)
    {
        return EnumerateGaps()
            .Select(g => (Raw: g, Visible: ClampGap(g, searchRange)))
            .Where(x => x.Visible is { } v && v.Range.Start >= currentTime)
            .OrderBy(x => x.Visible!.Value.Range.Start)
            .ThenBy(x => x.Visible!.Value.Range.End)
            .Select(x => (SceneGap?)x.Raw)
            .FirstOrDefault();
    }

    /// <summary>
    /// Returns the last gap (across all ZIndexes) that ends at or before
    /// <paramref name="currentTime"/>, or <see langword="null"/> when no such
    /// gap exists. A playhead sitting inside a gap ends before that gap, so it is
    /// skipped, but one resting exactly on a gap's end still finds it.
    /// </summary>
    /// <param name="searchRange">
    /// When set, a gap that does not intersect this range is dropped, and the filter/ordering use the
    /// gap's intersection with the range, so a gap straddling the range stays reachable through its
    /// visible portion. The returned gap is the raw gap, unclamped.
    /// </param>
    public SceneGap? FindPreviousGap(TimeSpan currentTime, TimeRange? searchRange = null)
    {
        return EnumerateGaps()
            .Select(g => (Raw: g, Visible: ClampGap(g, searchRange)))
            .Where(x => x.Visible is { } v && v.Range.End <= currentTime)
            .OrderByDescending(x => x.Visible!.Value.Range.End)
            .ThenByDescending(x => x.Visible!.Value.Range.Start)
            .Select(x => (SceneGap?)x.Raw)
            .FirstOrDefault();
    }

    /// <summary>
    /// Returns the gap on <paramref name="zIndex"/> that contains <paramref name="time"/> (half-open,
    /// like <see cref="TimeRange.Contains(TimeSpan)"/>), or <see langword="null"/> when the point is
    /// not inside a gap on that layer. Used to close the gap under a right-click position.
    /// </summary>
    public SceneGap? FindGapAt(TimeSpan time, int zIndex)
    {
        return EnumerateGaps()
            .Where(g => g.ZIndex == zIndex && g.Range.Contains(time))
            .Select(g => (SceneGap?)g)
            .FirstOrDefault();
    }

    // The gap clamped to its intersection with range (ZIndex and Anchor preserved), or null when they
    // do not overlap with positive width — half-open, matching TimeRange, so a point-only touch at an
    // edge yields no gap. A straddling gap contributes its in-range slice instead of being dropped.
    private static SceneGap? ClampGap(SceneGap gap, TimeRange? range)
    {
        if (range is not { } r) return gap;

        TimeSpan start = gap.Range.Start > r.Start ? gap.Range.Start : r.Start;
        TimeSpan end = gap.Range.End < r.End ? gap.Range.End : r.End;
        return end > start ? gap with { Range = new TimeRange(start, end - start) } : null;
    }
}
