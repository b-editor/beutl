using System.Collections.Immutable;
using Beutl.Audio;
using Beutl.Configuration;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public enum SilenceSplitMode { SplitOnly, DeleteSilence, RippleDelete }

public sealed partial class SilenceSplitService(HistoryManager history, IPreviewPlayer? player = null)
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
        => ApplyCore(scene, element, ranges, mode, includeLinked, history);

    private static void ApplyCore(Scene scene, Element element, IReadOnlyList<TimeRange> ranges, SilenceSplitMode mode,
        bool includeLinked, HistoryManager? recorder = null, CancellationToken cancellationToken = default,
        IProgress<int>? progress = null)
    {
        if (!TryResolveTargets(scene, element, ranges, includeLinked, out var group, out int groupIndex, out Element[] originals)) return;
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (originals.Any(scene.IsElementLocked)) throw new InvalidOperationException(Strings.SilenceSplit_LockedLinked);
        int[] layers = originals.Select(e => e.ZIndex.Value).Distinct().ToArray();
        TimeRange[] orderedRanges = ranges.OrderBy(r => r.Start).ToArray();
        List<Element> parts = [.. originals];
        // Portals and extension split callbacks may inspect other live scene elements while splitting.
        // Keep their normal Razor path; built-in media can be prepared without publishing intermediate clips.
        SplitBatch? batch = originals.All(e => e.Objects.All(o => o is not PortalObject)
            && ElementStructureService.OwnedSplittables(e).All(o => o is SourceSound or SceneSound or SceneDrawable || o.GetType() == typeof(SourceVideo)))
            ? new SplitBatch(scene) : null;
        MediaTime maximumEnd = originals.Max(e => e.Range.End);

        if (recorder is null) Edit();
        else recorder.ExecuteInTransaction(Edit, CommandNames.AutoSplitBySilence);

        void Edit()
        {
            // All cuts precede any movement, so every boundary remains in absolute scene time.
            MediaTime[] cuts = ranges.SelectMany(r => new[] { r.Start, r.End }).Distinct().OrderDescending().ToArray();
            int reported = 0;
            for (int i = 0; i < cuts.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                MediaTime cut = cuts[i];
                // Descending cuts only touch the original left pieces, never the added right pieces.
                Element[] targets = originals.Where(e => e.Start.Value < cut && cut < e.Range.End).ToArray();
                var split = ElementStructureService.SplitCore(scene, targets, cut, batch);
                // Razor may skip a cut that leaves less than a frame. Roll back all cuts rather
                // than silently deleting different intervals from the linked audio and video.
                if (split.Count != targets.Length) throw new InvalidOperationException(Strings.SilenceSplit_ShortSegment);
                parts.AddRange(split);
                // Leave 100 for the completed preparation, including deletion and ripple. Report
                // only percentage changes so a long recording cannot flood the UI dispatcher.
                int percent = (int)((long)(i + 1) * 99 / cuts.Length);
                if (percent > reported) { reported = percent; progress?.Report(percent); }
            }

            var removed = new List<Element>();
            int rangeIndex = 0;
            if (mode != SilenceSplitMode.SplitOnly)
                foreach (Element part in parts.OrderBy(e => e.Start.Value))
                {
                    while (rangeIndex < orderedRanges.Length && orderedRanges[rangeIndex].End <= part.Start.Value) rangeIndex++;
                    if (rangeIndex < orderedRanges.Length && part.Start.Value >= orderedRanges[rangeIndex].Start
                        && part.Range.End <= orderedRanges[rangeIndex].End) removed.Add(part);
                }
            var removedSet = removed.ToHashSet();
            Element[] removedFromScene = batch != null ? originals.Where(removedSet.Contains).ToArray() : removed.ToArray();
            foreach (Element part in removedFromScene) scene.RemoveChild(part);
            scene.RemoveElementsFromGroups(removedFromScene.Select(e => e.Id.Value).ToArray());
            Element[] pending = batch != null ? parts.Skip(originals.Length).Where(e => !removedSet.Contains(e)).ToArray() : [];
            if (mode == SilenceSplitMode.RippleDelete)
            {
                // Compute gaps and final positions while new pieces are still detached. History and
                // timeline observers see only the retained pieces at their final position.
                Element[] affected = scene.Children.Concat(pending).Where(e => layers.Contains(e.ZIndex.Value))
                    .OrderBy(e => e.Start.Value).ToArray();
                Ripple(scene, affected, CommonGaps(affected, orderedRanges).ToArray());
            }
            if (batch != null)
            {
                HashSet<Guid> removedIds = removed.Select(e => e.Id.Value).ToHashSet();
                var groups = batch.PendingGroups.AsEnumerable().Reverse().Select(g => g.Where(id => !removedIds.Contains(id)).ToImmutableHashSet())
                    .Where(g => g.Count >= 2).ToArray();
                bool originalGroupSurvives = group != null && group.Count(id => !removedIds.Contains(id)) >= 2;
                int index = Math.Min(groupIndex + (originalGroupSurvives ? 1 : 0), scene.Groups.Count);
                scene.Groups.InsertRange(index, groups);
                scene.Children.AddRange(pending);
                // Match AddChild's duration adjustment even when a split tail was subsequently removed.
                if (parts.Count > originals.Length && GlobalConfiguration.Instance.EditorConfig.AutoAdjustSceneDuration.Value
                    && maximumEnd > scene.Start.Value + scene.Duration.Value)
                    scene.Duration.Value = maximumEnd - scene.Start.Value;
            }
        }
    }

    // The clip and, when linked clips are included, the rest of its group. False when there is nothing to split.
    private static bool TryResolveTargets(Scene scene, Element element, IReadOnlyList<TimeRange> ranges, bool includeLinked,
        out ImmutableHashSet<Guid>? group, out int groupIndex, out Element[] originals)
    {
        group = null;
        groupIndex = -1;
        originals = [];
        if (!scene.Children.Contains(element) || scene.IsElementLocked(element) || scene.Uri == null || ranges.Count == 0) return false;

        ImmutableHashSet<Guid>? linked = includeLinked ? scene.Groups.FirstOrDefault(g => g.Contains(element.Id.Value)) : null;
        group = linked;
        groupIndex = linked == null ? scene.Groups.Count : scene.Groups.IndexOf(linked);
        originals = linked == null ? [element] : scene.Children.Where(e => linked.Contains(e.Id.Value)).ToArray();
        return true;
    }

    private static void Ripple(Scene scene, Element[] elements, TimeRange[] gaps)
    {
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

    private static IEnumerable<TimeRange> CommonGaps(Element[] elements, IReadOnlyList<TimeRange> ranges)
    {
        TimeRange[] occupied = elements.Select(e => e.Range).ToArray();
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
