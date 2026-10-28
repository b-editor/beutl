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
        List<Element> parts = group == null ? [element] : scene.Children.Where(e => group.Contains(e.Id.Value)).ToList();
        if (parts.Any(scene.IsElementLocked)) throw new InvalidOperationException(Strings.SilenceSplit_LockedLinked);
        int[] layers = parts.Select(e => e.ZIndex.Value).Distinct().ToArray();

        history.ExecuteInTransaction(() =>
        {
            // All cuts precede any movement, so every boundary remains in absolute scene time.
            foreach (MediaTime cut in ranges.SelectMany(r => new[] { r.Start, r.End }).Distinct().OrderDescending())
                parts.AddRange(ElementStructureService.SplitCore(scene, parts.ToArray(), cut));

            if (mode == SilenceSplitMode.SplitOnly) return;
            Element[] removed = parts.Where(e => ranges.Any(r => e.Start.Value >= r.Start && e.Range.End <= r.End)).ToArray();
            foreach (Element part in removed) scene.RemoveChild(part);
            scene.RemoveElementsFromGroups(removed.Select(e => e.Id.Value).ToArray());
            if (mode == SilenceSplitMode.RippleDelete)
            {
                // Collapse the same timeline intervals on every linked layer, including gaps before
                // a staggered clip. Remaining content is retained, not overlapped by the ripple.
                TimeRange[] gaps = CommonGaps(scene, layers, ranges).ToArray();
                foreach (var gap in gaps.OrderByDescending(r => r.End))
                {
                    // A lock anchors all linked layers at the same time, keeping later A/V in sync.
                    MediaTime barrier = scene.Children.Where(e => layers.Contains(e.ZIndex.Value)
                            && e.Start.Value >= gap.End && scene.IsElementLocked(e))
                        .Select(e => e.Start.Value).DefaultIfEmpty(MediaTime.MaxValue).Min();
                    Element[] anchored = scene.Children.Where(e => e.Start.Value >= barrier).ToArray();
                    foreach (int layer in layers)
                        RippleHelper.ShiftAfter(scene, layer, gap.End, -gap.Duration, anchored);
                }
            }
        }, CommandNames.AutoSplitBySilence);
    }

    private static IEnumerable<TimeRange> CommonGaps(Scene scene, int[] layers, IReadOnlyList<TimeRange> ranges)
    {
        TimeRange[] occupied = scene.Children.Where(e => layers.Contains(e.ZIndex.Value))
            .Select(e => e.Range).OrderBy(r => r.Start).ToArray();
        foreach (var range in ranges)
        {
            MediaTime cursor = range.Start;
            foreach (var clip in occupied)
            {
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
