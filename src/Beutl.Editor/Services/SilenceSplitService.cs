using System.Collections.Immutable;
using Beutl.Audio;
using Beutl.Configuration;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public enum SilenceSplitMode { SplitOnly, DeleteSilence, RippleDelete }

internal readonly record struct SilenceSplitRequest(Element Element, IReadOnlyList<TimeRange> Ranges);

public sealed partial class SilenceSplitService(HistoryManager history, EditorConfig configuration, IPreviewPlayer? player = null, DecoderRegistry? decoders = null)
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

    // The caller owns history or works on a detached scene.
    internal SilenceSplitRequest[] ApplyBatch(Scene scene, IReadOnlyList<SilenceSplitRequest> requests,
        SilenceSplitMode mode, bool includeLinked, CancellationToken cancellationToken, IProgress<int>? progress = null)
    {
        var applied = new List<SilenceSplitRequest>(requests.Count);
        List<(int[] Layers, TimeRange[] Ranges)>? deferred = mode == SilenceSplitMode.RippleDelete && requests.Count > 1 ? [] : null;
        foreach (SilenceSplitRequest request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ApplyCore(scene, request.Element, request.Ranges, mode, includeLinked,
                    cancellationToken: cancellationToken, progress: progress, deferredRipple: deferred))
            {
                applied.Add(request);
            }
        }

        if (deferred is { Count: > 0 }) RippleBatch(scene, deferred, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return [.. applied];
    }

    private bool ApplyCore(Scene scene, Element element, IReadOnlyList<TimeRange> ranges, SilenceSplitMode mode,
        bool includeLinked, HistoryManager? recorder = null, CancellationToken cancellationToken = default,
        IProgress<int>? progress = null, List<(int[] Layers, TimeRange[] Ranges)>? deferredRipple = null)
    {
        if (!TryResolveTargets(scene, element, ranges, includeLinked, out var group, out int groupIndex, out Element[] originals)) return false;
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (originals.Any(scene.IsElementLocked)) throw new InvalidOperationException(Strings.SilenceSplit_LockedLinked);
        int[] layers = originals.Select(e => e.ZIndex.Value).Distinct().Order().ToArray();
        TimeRange[] orderedRanges = ranges.OrderBy(r => r.Start).ToArray();
        List<Element> parts = [.. originals];
        // Portals, extension split callbacks and the expressions a cut reads may inspect other live scene elements while
        // splitting. Keep their normal Razor path; built-in media can be prepared without publishing intermediate clips.
        SplitBatch? batch = originals.All(e => e.Objects.All(o => o is not PortalObject
                && !(o is DrawableTimeController controller && MediaTimeMapping.HasTimeExpression(controller)))
            && ElementStructureService.OwnedSplittables(e).All(o => (o is SourceSound or SceneSound or SceneDrawable || o.GetType() == typeof(SourceVideo))
                && o is not ITimeMappedSource { Speed.HasExpression: true }))
            ? new SplitBatch(scene) : null;
        MediaTime maximumEnd = originals.Max(e => e.Range.End);
        bool changed = false;

        if (recorder is null) Edit();
        else recorder.ExecuteInTransaction(Edit, CommandNames.AutoSplitBySilence);
        return changed;

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
                var split = ElementStructureService.SplitCore(scene, targets, cut, batch, configuration.AutoAdjustSceneDuration.Value, decoders);
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
            changed = parts.Count > originals.Length || removed.Count > 0;
            Element[] removedFromScene = batch != null ? originals.Where(removedSet.Contains).ToArray() : removed.ToArray();
            foreach (Element part in removedFromScene) scene.RemoveChild(part);
            scene.RemoveElementsFromGroups(removedFromScene.Select(e => e.Id.Value).ToArray());
            Element[] pending = batch != null ? parts.Skip(originals.Length).Where(e => !removedSet.Contains(e)).ToArray() : [];
            if (mode == SilenceSplitMode.RippleDelete)
            {
                // A batch must finish its cuts before moving any clips. Source mapping and
                // silence analysis can both depend on the timeline clock.
                if (deferredRipple == null) RippleGaps(scene, layers, orderedRanges, pending);
                else if (changed) deferredRipple.Add((layers, orderedRanges));
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
                if (parts.Count > originals.Length) scene.ExtendDurationToCover(maximumEnd, configuration.AutoAdjustSceneDuration.Value);
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

        ImmutableHashSet<Guid>? linked = includeLinked ? scene.FindGroup(element.Id.Value) : null;
        group = linked;
        groupIndex = linked == null ? scene.Groups.Count : scene.Groups.IndexOf(linked);
        originals = linked == null ? [element] : scene.Children.Where(e => linked.Contains(e.Id.Value)).ToArray();
        return true;
    }

    private static void RippleBatch(Scene scene, List<(int[] Layers, TimeRange[] Ranges)> requests,
        CancellationToken cancellationToken)
    {
        // Find common gaps and lock barriers on the original timeline before moving any clip.
        var elementsByLayer = scene.Children.ToLookup(element => element.ZIndex.Value);
        var windowsByLayer = new Dictionary<int, List<RippleWindow>>();
        foreach (var requestsOnLayers in requests.GroupBy(request => request.Layers, s_layerSetComparer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ranges = new List<TimeRange>();
            foreach (TimeRange range in requestsOnLayers.SelectMany(request => request.Ranges).OrderBy(range => range.Start))
            {
                if (ranges.Count > 0 && range.Start <= ranges[^1].End) ranges[^1] = ranges[^1].Union(range);
                else ranges.Add(range);
            }

            Element[] affected = requestsOnLayers.Key.SelectMany(layer => elementsByLayer[layer])
                .OrderBy(element => element.Start.Value).ToArray();
            MediaTime[] barriers = affected.Where(scene.IsElementLocked).Select(element => element.Start.Value).ToArray();
            var windows = new List<RippleWindow>();
            int nextBarrier = 0;
            foreach (TimeRange gap in CommonGaps(affected, ranges))
            {
                while (nextBarrier < barriers.Length && barriers[nextBarrier] < gap.End) nextBarrier++;
                MediaTime? barrier = nextBarrier < barriers.Length ? barriers[nextBarrier] : null;
                if (barrier != gap.End) windows.Add(new RippleWindow(gap, barrier));
            }

            if (windows.Count == 0) continue;
            foreach (int layer in requestsOnLayers.Key)
            {
                if (!windowsByLayer.TryGetValue(layer, out var layerWindows)) windowsByLayer.Add(layer, layerWindows = []);
                layerWindows.AddRange(windows);
            }
        }

        foreach ((int layer, List<RippleWindow> windows) in windowsByLayer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RippleLayer(scene, elementsByLayer[layer], windows);
        }
    }

    private readonly record struct RippleWindow(TimeRange Gap, MediaTime? Barrier);

    // Close each part of the overlap once. A linked lock stops its request while another
    // request covering the same part can still move clips on this layer.
    private static void RippleLayer(Scene scene, IEnumerable<Element> elements, List<RippleWindow> windows)
    {
        windows.Sort(static (left, right) => left.Gap.Start.CompareTo(right.Gap.Start));
        MediaTime[] edges = windows.Select(window => window.Gap.Start).Concat(windows.Select(window => window.Gap.End))
            .Distinct().Order().ToArray();
        var active = new PriorityQueue<RippleWindow, (bool Unbounded, MediaTime Barrier)>(
            Comparer<(bool, MediaTime)>.Create(static (left, right) => right.CompareTo(left)));
        var shifts = new SortedDictionary<MediaTime, MediaTime>();
        int next = 0;
        for (int i = 1; i < edges.Length; i++)
        {
            MediaTime start = edges[i - 1];
            while (next < windows.Count && windows[next].Gap.Start == start)
            {
                RippleWindow window = windows[next++];
                active.Enqueue(window, (!window.Barrier.HasValue, window.Barrier.GetValueOrDefault()));
            }
            while (active.TryPeek(out RippleWindow ended, out _) && ended.Gap.End <= start) active.Dequeue();
            if (!active.TryPeek(out RippleWindow applicable, out _)) continue;

            // There is no retained clip start inside this part of the gap.
            MediaTime end = edges[i];
            MediaTime duration = end - start;
            shifts[end] = shifts.GetValueOrDefault(end) + duration;
            if (applicable.Barrier is { } barrier) shifts[barrier] = shifts.GetValueOrDefault(barrier) - duration;
        }

        var changes = shifts.ToArray();
        int changeIndex = 0;
        MediaTime shift = MediaTime.Zero;
        foreach (Element element in elements.OrderBy(element => element.Start.Value))
        {
            while (changeIndex < changes.Length && changes[changeIndex].Key <= element.Start.Value)
                shift += changes[changeIndex++].Value;
            if (shift > MediaTime.Zero && !scene.IsElementLocked(element)) element.Start.Value -= shift;
        }
    }

    private static readonly EqualityComparer<int[]> s_layerSetComparer = EqualityComparer<int[]>.Create(
        static (left, right) => left.AsSpan().SequenceEqual(right),
        static layers =>
        {
            var hash = new HashCode();
            foreach (int layer in layers) hash.Add(layer);
            return hash.ToHashCode();
        });

    private static void RippleGaps(Scene scene, int[] layers, IReadOnlyList<TimeRange> ranges, Element[] pending)
    {
        Element[] affected = scene.Children.Concat(pending).Where(e => layers.Contains(e.ZIndex.Value))
            .OrderBy(e => e.Start.Value).ToArray();
        Ripple(scene, affected, CommonGaps(affected, ranges).ToArray());
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
