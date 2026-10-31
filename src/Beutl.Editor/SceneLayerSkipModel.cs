using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor;

// Export preflight skips layers with SceneCompositor's own SceneLayerSnapshot, so it excludes exactly the
// elements the compositor would. Shared by the root-scene walk (ExportSourceValidator) and the referenced-scene
// walk (ProxySourceEnumerator) so a muted/non-solo layer inside an embedded scene is skipped too.
internal static class SceneLayerSkipModel
{
    // Both preflight entry points follow the same output and matte dependencies. A matte ignores solo
    // and output visibility, but still obeys enable/mute and is needed only while its consumer is active.
    public static IEnumerable<(Element Element, TimeRange? Window, MediaTime? FrameOrigin)> EnumerateRenderableElements(
        Scene scene, CompositionTarget target, TimeRange? window, Rational? frameRate = null, MediaTime? frameOrigin = null)
    {
        SceneLayerSnapshot layers = SceneLayerSnapshot.Build(scene);
        var visiting = new HashSet<Element>();
        HashSet<Element> participants = target == CompositionTarget.Graphics ? ElementTransitions.GetParticipants(scene) : [];
        Rational rate = frameRate ?? scene.FindHierarchicalParent<Project>().GetFrameRate();
        TimeRange? replayWindow = null;
        foreach (Element element in scene.Children)
            foreach (var item in Visit(element, window, false, frameOrigin))
                yield return item;

        IEnumerable<(Element, TimeRange?, MediaTime?)> Visit(
            Element element, TimeRange? requested, bool matte, MediaTime? origin, bool remapped = false)
        {
            if (!element.IsEnabled.Value
                || layers.ShouldSkip(element.ZIndex.Value, target, ignoreSolo: matte)
                || (target == CompositionTarget.Graphics && !matte && !element.IsVisibleInOutput.Value))
                yield break;

            if (!visiting.Add(element)) yield break;
            try
            {
                foreach ((TimeRange frameWindow, TimeRange contentWindow) in EvaluationWindows(element, requested))
                {
                    MediaTime? dependencyOrigin = remapped ? null : origin;
                    yield return (element, remapped ? null : contentWindow, dependencyOrigin);
                    if (target == CompositionTarget.Graphics && element.MatteMode.Value != MatteMode.None)
                    {
                        // A controller replays a captured clip outside its authored range. Its content
                        // gets a full walk and its matte is selected across all possible scene times.
                        TimeRange matteWindow = remapped
                            ? replayWindow ??= scene.Children.Select(e => e.Range).Aggregate((a, b) => a.Union(b))
                            : frameWindow;
                        foreach ((int source, TimeRange sourceWindow) in MatteWindows(element, matteWindow, dependencyOrigin))
                        {
                            foreach (Element candidate in scene.Children)
                            {
                                if (candidate.ZIndex.Value != source) continue;
                                foreach (var item in Visit(candidate, sourceWindow, true, dependencyOrigin))
                                    yield return item;
                            }
                        }
                    }

                    // A portal's own Count uses its side's held clock; its imported elements and
                    // matte sources play at frame time, as in CompositorContext.EvaluateElementIntoFlow.
                    if (target == CompositionTarget.Graphics)
                    {
                        foreach (PortalObject portal in element.Objects.OfType<PortalObject>())
                        {
                            if (!portal.IsEnabled.Value) continue;
                            // A later controller can consume these portal inputs through the flow,
                            // even with no explicit Target. Keep their original import window, then
                            // widen the replayed inputs rather than every output layer in the scene.
                            bool remapsInputs = remapped || element.Objects.SkipWhile(obj => obj != portal).Skip(1)
                                .OfType<DrawableTimeController>().Any(controller => controller.IsEnabled.Value);
                            if (!matte && !remapsInputs) continue;
                            foreach ((int? count, TimeRange inputs) in PortalWindows(portal, frameWindow, contentWindow, origin))
                            {
                                int start = portal.ZIndex.Value + 1;
                                int? end = portal.ZIndex.Value + count;
                                foreach (Element candidate in scene.Children)
                                {
                                    if (candidate.ZIndex.Value < start || (end is { } last && candidate.ZIndex.Value > last)) continue;
                                    foreach (var item in Visit(candidate, inputs, matte, origin, remapsInputs))
                                        yield return item;
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                visiting.Remove(element);
            }
        }

        IEnumerable<(int Source, TimeRange Window)> MatteWindows(Element element, TimeRange frames, MediaTime? origin)
        {
            if (frames.IsEmpty || element.MatteSourceKind.Value == MatteSourceKind.Layer)
            {
                if (element.GetMatteSourceZIndex(scene, frames.Start) is { } source) yield return (source, frames);
                yield break;
            }

            // A relative source can change only where a clip begins or ends. Resolve each interval
            // with the compositor's same point query instead of sampling every export frame.
            var edges = new SortedSet<MediaTime> { frames.Start, frames.End };
            foreach (Element candidate in scene.Children)
            {
                if (candidate.Start.Value > frames.Start && candidate.Start.Value < frames.End) edges.Add(candidate.Start.Value);
                if (candidate.Range.End > frames.Start && candidate.Range.End < frames.End) edges.Add(candidate.Range.End);
            }

            MediaTime start = frames.Start;
            foreach (MediaTime end in edges.Skip(1))
            {
                TimeRange interval = TimeRange.FromRange(start, end);
                MediaTime sample = start;
                start = end;
                // Known exports need only intervals containing an output frame. A time-remapped full
                // walk has no frame grid and keeps every interval that could be sampled.
                if (origin is { } frameStart)
                {
                    long frame = (sample - frameStart).ToFrame(rate, MidpointRounding.ToPositiveInfinity);
                    sample = frameStart + MediaTime.FromFrames(frame, rate);
                }

                if (sample < end && element.GetMatteSourceZIndex(scene, sample) is { } source)
                    yield return (source, interval);
            }
        }

        IEnumerable<(TimeRange Frame, TimeRange Content)> EvaluationWindows(Element element, TimeRange? requested)
        {
            TransitionBoundary? start = participants.Contains(element) ? ElementTransitions.GetBoundaryAtStart(element) : null;
            TransitionBoundary? end = participants.Contains(element) ? ElementTransitions.GetBoundaryAtEnd(element) : null;
            TransitionBoundary[] boundaries = new[] { start, end }.OfType<TransitionBoundary>().ToArray();
            TimeRange requestedWindow = requested ?? boundaries.Aggregate(element.Range, (range, boundary) => range.Union(boundary.Region));
            TimeRange ordinary = element.Range;
            // After an overlapping boundary ends, the incoming element has taken over.
            if (end is { Incoming: not null } outgoingBoundary && outgoingBoundary.Region.End < ordinary.End)
                ordinary = TimeRange.FromRange(ordinary.Start, outgoingBoundary.Region.End);

            if (requestedWindow.IsEmpty)
            {
                foreach (TransitionBoundary boundary in boundaries)
                {
                    if (!boundary.Region.Contains(requestedWindow.Start)) continue;
                    MediaTime time = boundary.Outgoing == element
                        ? boundary.GetOutgoingTime(requestedWindow.Start) : boundary.GetIncomingTime(requestedWindow.Start);
                    yield return (requestedWindow, new TimeRange(time, MediaTime.Zero));
                    yield break;
                }
                if (ordinary.Contains(requestedWindow.Start)) yield return (requestedWindow, requestedWindow);
                yield break;
            }

            MediaTime cursor = requestedWindow.Start;
            foreach (TransitionBoundary boundary in boundaries)
            {
                TimeRange active = boundary.Region.Intersect(requestedWindow);
                if (active.IsEmpty) continue;
                TimeRange before = ordinary.Intersect(TimeRange.FromRange(cursor, active.Start));
                if (!before.IsEmpty) yield return (before, before);

                bool outgoing = boundary.Outgoing == element;
                MediaTime heldTime = outgoing
                    ? boundary.GetOutgoingTime(boundary.Region.End) : boundary.GetIncomingTime(boundary.Region.Start);
                MediaTime split = heldTime < active.Start ? active.Start : heldTime > active.End ? active.End : heldTime;
                TimeRange first = TimeRange.FromRange(active.Start, split);
                TimeRange last = TimeRange.FromRange(split, active.End);
                if (!first.IsEmpty) yield return (first, outgoing ? first : new TimeRange(heldTime, MediaTime.Zero));
                if (!last.IsEmpty) yield return (last, outgoing ? new TimeRange(heldTime, MediaTime.Zero) : last);
                cursor = active.End;
            }
            TimeRange after = ordinary.Intersect(TimeRange.FromRange(cursor, requestedWindow.End));
            if (!after.IsEmpty) yield return (after, after);
        }

        IEnumerable<(int? Count, TimeRange Window)> PortalWindows(
            PortalObject portal, TimeRange frames, TimeRange content, MediaTime? frameOrigin)
        {
            var context = new CompositionContext(content.Start) { FrameRate = rate };
            if (portal.Count.Expression == null || content.IsEmpty)
            {
                yield return (context.Get(portal.Count), frames);
                yield break;
            }

            // A full walk after a time remap has no known frame grid. A Count expression can
            // import any following layer between the original frames; null keeps all candidates.
            if (frameOrigin is not { } origin)
            {
                yield return (null, frames);
                yield break;
            }

            // Sample the output's frame grid, not a new grid starting at the clipped element window.
            // Keep point windows: an input active only between rendered frames must not block export.
            long first = (frames.Start - origin).ToFrame(rate, MidpointRounding.ToPositiveInfinity);
            for (long frame = first; ; frame++)
            {
                MediaTime time = origin + MediaTime.FromFrames(frame, rate);
                if (time >= frames.End) break;
                context.Time = time;
                yield return (context.Get(portal.Count), new TimeRange(time, MediaTime.Zero));
            }
        }
    }
}
