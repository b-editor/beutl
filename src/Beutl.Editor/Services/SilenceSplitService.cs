using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public enum SilenceSplitMode { SplitOnly, DeleteSilence, RippleDelete }

public sealed class SilenceSplitService(HistoryManager history)
{
    // Round inward so frame alignment cannot eat the protected audio. Preview and apply use this
    // same plan; off-grid clip edges stay exact and no sub-frame fragments are created.
    public static IReadOnlyList<TimeRange> Plan(Scene scene, Element element, IReadOnlyList<TimeRange> localRanges)
    {
        var result = new List<TimeRange>();
        Rational rate = SceneTimeRangeService.GetFrameRate(scene);
        MediaTime frame = SceneTimeRangeService.GetFrameDuration(scene);
        foreach (var range in localRanges.OrderBy(r => r.Start))
        {
            var start = element.Start.Value + range.Start;
            var end = element.Start.Value + range.End;
            start = start <= element.Start.Value ? element.Start.Value : start.RoundToFrame(rate, MidpointRounding.ToPositiveInfinity);
            end = end >= element.Range.End ? element.Range.End : end.RoundToFrame(rate, MidpointRounding.ToNegativeInfinity);
            if (end - start < frame) continue;
            if (start > element.Start.Value && !ElementStructureService.LeavesAFrameOnEachSide(scene, element, start)) continue;
            if (end < element.Range.End && !ElementStructureService.LeavesAFrameOnEachSide(scene, element, end)) continue;
            if (result.Count > 0 && start <= result[^1].End)
                result[^1] = TimeRange.FromRange(result[^1].Start, end > result[^1].End ? end : result[^1].End);
            else result.Add(TimeRange.FromRange(start, end));
        }
        return result;
    }

    public void Apply(Scene scene, Element element, IReadOnlyList<TimeRange> ranges, SilenceSplitMode mode, bool includeLinked)
    {
        if (!scene.Children.Contains(element) || scene.IsElementLocked(element) || scene.Uri == null || ranges.Count == 0) return;
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var group = includeLinked ? scene.Groups.FirstOrDefault(g => g.Contains(element.Id.Value)) : null;
        Element[] originals = group == null ? [element] : scene.Children.Where(e => group.Contains(e.Id.Value)).ToArray();
        if (originals.Any(scene.IsElementLocked)) throw new InvalidOperationException(Strings.SilenceSplit_LockedLinked);
        int[] layers = originals.Select(e => e.ZIndex.Value).Distinct().ToArray();
        TimeRange[] orderedRanges = ranges.OrderBy(r => r.Start).ToArray();
        List<Element> parts = [.. originals];

        history.ExecuteInTransaction(() =>
        {
            // All cuts precede any movement, so every boundary remains in absolute scene time.
            foreach (MediaTime cut in ranges.SelectMany(r => new[] { r.Start, r.End }).Distinct().OrderDescending())
            {
                // Descending cuts only touch the original left pieces, never the added right pieces.
                Element[] targets = originals.Where(e => e.Start.Value < cut && cut < e.Range.End).ToArray();
                var split = ElementStructureService.SplitCore(scene, targets, cut);
                // Razor may skip a cut that leaves less than a frame. Roll back all cuts rather
                // than silently deleting different intervals from the linked audio and video.
                if (split.Count != targets.Length) throw new InvalidOperationException(Strings.SilenceSplit_ShortSegment);
                parts.AddRange(split);
            }

            if (mode == SilenceSplitMode.SplitOnly) return;
            var removed = new List<Element>();
            int rangeIndex = 0;
            foreach (Element part in parts.OrderBy(e => e.Start.Value))
            {
                while (rangeIndex < orderedRanges.Length && orderedRanges[rangeIndex].End <= part.Start.Value) rangeIndex++;
                if (rangeIndex < orderedRanges.Length && part.Start.Value >= orderedRanges[rangeIndex].Start
                    && part.Range.End <= orderedRanges[rangeIndex].End) removed.Add(part);
            }
            foreach (Element part in removed) scene.RemoveChild(part);
            scene.RemoveElementsFromGroups(removed.Select(e => e.Id.Value).ToArray());
            if (mode == SilenceSplitMode.RippleDelete)
            {
                // Collapse the same timeline intervals on every linked layer, including gaps before
                // a staggered clip. Remaining content is retained, not overlapped by the ripple.
                Ripple(scene, layers, CommonGaps(scene, layers, orderedRanges).ToArray());
            }
        }, CommandNames.AutoSplitBySilence);
    }

    private static void Ripple(Scene scene, int[] layers, TimeRange[] gaps)
    {
        Element[] elements = scene.Children.Where(e => layers.Contains(e.ZIndex.Value)).OrderBy(e => e.Start.Value).ToArray();
        MediaTime[] barriers = elements.Where(scene.IsElementLocked).Select(e => e.Start.Value).ToArray();
        int gapIndex = 0, barrierIndex = 0;
        MediaTime shift = MediaTime.Zero;
        foreach (Element part in elements)
        {
            // A lock anchors every linked layer. Only gaps after that anchor move later clips.
            while (barrierIndex < barriers.Length && barriers[barrierIndex] <= part.Start.Value)
            {
                MediaTime barrier = barriers[barrierIndex++];
                shift = MediaTime.Zero;
                while (gapIndex < gaps.Length && gaps[gapIndex].End <= barrier) gapIndex++;
            }
            while (gapIndex < gaps.Length && gaps[gapIndex].End <= part.Start.Value)
                shift += gaps[gapIndex++].Duration;

            // CommonGaps is empty on all affected layers, so collapsing it cannot cross retained content.
            // Write once per clip, avoiding quadratic history entries and timeline notifications.
            if (shift > MediaTime.Zero && !scene.IsElementLocked(part)) part.Start.Value -= shift;
        }
    }

    private static IEnumerable<TimeRange> CommonGaps(Scene scene, int[] layers, IReadOnlyList<TimeRange> ranges)
    {
        TimeRange[] occupied = scene.Children.Where(e => layers.Contains(e.ZIndex.Value))
            .Select(e => e.Range).OrderBy(r => r.Start).ToArray();
        int firstOccupied = 0;
        foreach (var range in ranges)
        {
            MediaTime cursor = range.Start;
            while (firstOccupied < occupied.Length && occupied[firstOccupied].End <= cursor) firstOccupied++;
            for (int i = firstOccupied; i < occupied.Length; i++)
            {
                var clip = occupied[i];
                if (clip.End <= cursor) continue;
                if (clip.Start >= range.End) break;
                if (clip.Start > cursor) yield return TimeRange.FromRange(cursor, clip.Start);
                if (clip.End > cursor) cursor = clip.End;
                if (cursor >= range.End) break;
            }
            if (cursor < range.End) yield return TimeRange.FromRange(cursor, range.End);
        }
    }
}
