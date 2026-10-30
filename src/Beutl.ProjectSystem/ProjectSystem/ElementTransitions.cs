using Beutl.Collections.Pooled;
using Beutl.Graphics.Transitions;
using Beutl.Media;

namespace Beutl.ProjectSystem;

// Resolves the transitions at clip boundaries: which elements they blend, over which span, and how far
// they have played at a given time.
internal static class ElementTransitions
{
    private static readonly HashSet<Element> s_noParticipants = [];
    // Clip boundaries snap to frames, but a time typed by hand or read outside a project can leave one element's
    // end just off the next element's start. Anything closer than this is one boundary.
    internal static readonly MediaTime AdjacencyTolerance = MediaTime.FromSeconds(0.001);

    // Whether outgoing and incoming meet at a boundary: enabled, on the same layer, incoming starting
    // after outgoing and no later than its end, and outgoing not reaching past incoming's end.
    public static bool AreAdjacent(Element outgoing, Element incoming)
    {
        return outgoing != incoming
               && outgoing.IsEnabled.Value
               && incoming.IsEnabled.Value
               && outgoing.ZIndex.Value == incoming.ZIndex.Value
               && outgoing.Start.Value < incoming.Start.Value
               && incoming.Start.Value <= outgoing.Range.End + AdjacencyTolerance
               && outgoing.Range.End <= incoming.Range.End + AdjacencyTolerance;
    }

    // The element whose end meets element's start and that meets no other element's start first, so each
    // boundary pairs two elements that choose each other. When several meet element's start, the one that
    // ends last wins, since it is the one visible just before the boundary.
    public static Element? FindPrevious(Element element)
    {
        Element? previous = FindPreviousCandidate(element);
        return previous != null && FindNextCandidate(previous) == element ? previous : null;
    }

    // The element whose start meets element's end and that no other element meets first; when several
    // start there, the one that starts first wins.
    public static Element? FindNext(Element element)
    {
        Element? next = FindNextCandidate(element);
        return next != null && FindPreviousCandidate(next) == element ? next : null;
    }

    private static Element? FindPreviousCandidate(Element element)
    {
        if (element.HierarchicalParent is not Scene scene) return null;

        Element? previous = null;
        SceneElementIndex index = scene.ElementIndex;
        using var candidates = new PooledList<Element>();
        index.Query(element.Start.Value - AdjacencyTolerance, element.Range.End + AdjacencyTolerance,
            candidates, element.ZIndex.Value);
        foreach (Element item in candidates)
        {
            if (AreAdjacent(item, element) && (previous == null || item.Range.End > previous.Range.End
                || item.Range.End == previous.Range.End && index.CompareSceneOrder(item, previous) < 0))
            {
                previous = item;
            }
        }

        return previous;
    }

    private static Element? FindNextCandidate(Element element)
    {
        if (element.HierarchicalParent is not Scene scene) return null;

        Element? next = null;
        SceneElementIndex index = scene.ElementIndex;
        using var candidates = new PooledList<Element>();
        index.Query(element.Start.Value - AdjacencyTolerance, element.Range.End + AdjacencyTolerance,
            candidates, element.ZIndex.Value);
        foreach (Element item in candidates)
        {
            if (AreAdjacent(element, item) && (next == null || item.Start.Value < next.Start.Value
                || item.Start.Value == next.Start.Value && index.CompareSceneOrder(item, next) < 0))
            {
                next = item;
            }
        }

        return next;
    }

    // The transition at element's start, shared with the element before it when there is one.
    public static TransitionBoundary? GetBoundaryAtStart(Element element, TransitionDurationOverride? durationOverride = null)
    {
        if (!element.IsEnabled.Value) return null;

        return CreateBoundary(FindPrevious(element), element, durationOverride);
    }

    // The transition at element's end, shared with the element after it when there is one.
    public static TransitionBoundary? GetBoundaryAtEnd(Element element, TransitionDurationOverride? durationOverride = null)
    {
        if (!element.IsEnabled.Value) return null;

        return CreateBoundary(element, FindNext(element), durationOverride);
    }

    // The boundary transition element takes part in at time, if any. Spans never overlap within an
    // element, so there is at most one.
    public static bool TryGetActive(Element element, MediaTime time, out TransitionBoundary boundary)
    {
        if (GetBoundaryAtStart(element) is { } start && start.Region.Contains(time))
        {
            boundary = start;
            return true;
        }

        if (GetBoundaryAtEnd(element) is { } end && end.Region.Contains(time))
        {
            boundary = end;
            return true;
        }

        boundary = default;
        return false;
    }

    // The elements that can take part in a boundary transition: those that set an active side, and those
    // adjacent across such a side. No other element has a boundary at either end, so callers that look
    // at every element skip the neighbour searches for the rest; a scene without transitions costs one
    // pass.
    public static HashSet<Element> GetParticipants(Scene scene)
        => scene.HasTransitions ? scene.ElementIndex.GetTransitionParticipants() : s_noParticipants;

    internal static HashSet<Element> CollectParticipants(IEnumerable<Element> children, SceneElementIndex index)
    {
        HashSet<Element>? participants = null;
        using var candidates = new PooledList<Element>();
        foreach (Element element in children)
        {
            bool enter = IsActive(element.EnterTransition.Value);
            bool exit = IsActive(element.ExitTransition.Value);
            if (!enter && !exit) continue;

            participants ??= [];
            participants.Add(element);
            candidates.Clear();
            index.Query(element.Start.Value - AdjacencyTolerance, element.Range.End + AdjacencyTolerance,
                candidates, element.ZIndex.Value);
            foreach (Element other in candidates)
            {
                if ((enter && AreAdjacent(other, element)) || (exit && AreAdjacent(element, other)))
                {
                    participants.Add(other);
                }
            }
        }

        return participants ?? [];
    }

    // The spans of every boundary transition that overlap range or touch either end of it. A transition
    // draws its elements past their own ranges, so its frames change whenever theirs do.
    // Reuse indexed single-range queries and the cached participant set for a bulk edit.
    public static List<TimeRange> GetRegionsNear(Scene scene, IReadOnlyList<TimeRange> ranges)
    {
        if (!scene.HasTransitions || ranges.Count == 0) return [];
        var regions = new HashSet<TimeRange>();
        foreach (TimeRange range in ranges) regions.UnionWith(GetRegionsNear(scene, range));
        return [.. regions];
    }

    public static List<TimeRange> GetRegionsNear(Scene scene, TimeRange range, int? layer = null)
    {
        if (!scene.HasTransitions) return [];
        var regions = new HashSet<TimeRange>();
        // Per-element range changes already know the affected layer. Avoid rebuilding the global
        // participant set for every step of a bulk edit or history replay.
        HashSet<Element>? participants = layer is null ? GetParticipants(scene) : null;
        if (participants?.Count == 0) return [];
        using var candidates = new PooledList<Element>();
        scene.ElementIndex.Query(range.Start - AdjacencyTolerance, range.End + AdjacencyTolerance, candidates, layer);
        foreach (Element element in candidates)
        {
            if (participants is not null && !participants.Contains(element)) continue;
            if (GetBoundaryAtStart(element) is { } start && Touches(start.Region, range))
            {
                regions.Add(start.Region);
            }

            // The incoming side may start after this range, so inspect both boundaries of candidates.
            if (GetBoundaryAtEnd(element) is { } end && Touches(end.Region, range))
            {
                regions.Add(end.Region);
            }

            // The span at one end of an element stops at its middle while a transition also runs at the
            // other end, so when a neighbour arrives at or leaves that other end, the span can grow or
            // shrink anywhere in the element. The scene has already changed by now, so the span it had
            // cannot be recovered; the whole element counts instead.
            if ((Reaches(range, element.Range.End) && HasStartTransition(element, null))
                || (Reaches(range, element.Start.Value) && HasEndTransition(element, null)))
            {
                regions.Add(element.Range);
            }
        }

        return [.. regions];
    }

    // The time of the last frame the timeline shows inside element, so a held frame matches the one the
    // viewer saw right before the boundary instead of the one past the out point.
    public static MediaTime GetLastFrameTime(Element element)
    {
        MediaTime time = element.Range.End - MediaTime.FromFrames(1, GetFrameRate(element));
        return time < element.Start.Value ? element.Start.Value : time;
    }

    private static bool Touches(TimeRange a, TimeRange b)
    {
        return a.Start <= b.End && b.Start <= a.End;
    }

    // Whether range reaches time, as a neighbour that meets an element's end there does.
    private static bool Reaches(TimeRange range, MediaTime time)
    {
        return range.Start <= time + AdjacencyTolerance && time - AdjacencyTolerance <= range.End;
    }

    private static bool IsActive(ClipTransition? transition) => transition is { IsEnabled.Value: true };

    // An element's start counts as transitioning when the boundary there spans some time: a side adds a
    // duration, or the element before it overlaps it while either side sets a transition. Likewise for
    // its end. A transition of no length leaves the span at the other end whole.
    private static bool HasStartTransition(Element element, TransitionDurationOverride? durationOverride)
    {
        Element? previous = FindPrevious(element);
        return HasSpan(previous, element, previous?.ExitTransition.Value, element.EnterTransition.Value, durationOverride);
    }

    private static bool HasEndTransition(Element element, TransitionDurationOverride? durationOverride)
    {
        Element? next = FindNext(element);
        return HasSpan(element, next, element.ExitTransition.Value, next?.EnterTransition.Value, durationOverride);
    }

    private static bool HasSpan(
        Element? outgoing,
        Element? incoming,
        ClipTransition? exit,
        ClipTransition? enter,
        TransitionDurationOverride? durationOverride)
    {
        if (!IsActive(exit) && !IsActive(enter)) return false;

        return GetDuration(exit, durationOverride) > MediaTime.Zero
               || GetDuration(enter, durationOverride) > MediaTime.Zero
               || (outgoing != null && incoming != null && incoming.Start.Value < outgoing.Range.End);
    }

    private static MediaTime GetDuration(ClipTransition? transition, TransitionDurationOverride? durationOverride)
    {
        if (!IsActive(transition)) return MediaTime.Zero;

        MediaTime duration = durationOverride is { } value && ReferenceEquals(value.Transition, transition)
            ? value.Duration
            : transition!.Duration.Value;
        return duration < MediaTime.Zero ? MediaTime.Zero : duration;
    }

    private static TransitionBoundary? CreateBoundary(
        Element? outgoing, Element? incoming, TransitionDurationOverride? durationOverride)
    {
        ClipTransition? exit = outgoing?.ExitTransition.Value;
        ClipTransition? enter = incoming?.EnterTransition.Value;
        // The incoming side decides how the two blend; the outgoing side then only adds its duration.
        ClipTransition? transition = IsActive(enter) ? enter : IsActive(exit) ? exit : null;
        if (transition == null) return null;

        MediaTime start;
        MediaTime end;
        if (outgoing != null && incoming != null)
        {
            // Centred on the cut when both sides add the same duration, ahead of or behind it when only
            // one does, and covering the overlap when the two elements overlap.
            start = Min(outgoing.Range.End - GetDuration(exit, durationOverride), incoming.Start.Value);
            end = Max(incoming.Start.Value + GetDuration(enter, durationOverride), outgoing.Range.End);
        }
        else if (outgoing != null)
        {
            start = outgoing.Range.End - GetDuration(exit, durationOverride);
            end = outgoing.Range.End;
        }
        else
        {
            start = incoming!.Start.Value;
            end = incoming.Start.Value + GetDuration(enter, durationOverride);
        }

        // A span never leaves its elements, and stops at the middle of an element that also transitions
        // at its other end, so the spans at an element's two ends never overlap.
        if (outgoing != null)
        {
            start = Max(start, outgoing.Start.Value);
            if (HasStartTransition(outgoing, durationOverride))
            {
                start = Max(start, outgoing.Start.Value + new MediaTime(outgoing.Length.Value.Flicks / 2));
            }
        }

        if (incoming != null)
        {
            end = Min(end, incoming.Range.End);
            if (HasEndTransition(incoming, durationOverride))
            {
                end = Min(end, incoming.Start.Value + new MediaTime(incoming.Length.Value.Flicks / 2));
            }
        }

        if (end <= start) return null;

        return new TransitionBoundary(outgoing, incoming, transition, TimeRange.FromRange(start, end));
    }

    private static MediaTime Min(MediaTime a, MediaTime b) => a < b ? a : b;

    private static MediaTime Max(MediaTime a, MediaTime b) => a > b ? a : b;

    private static Rational GetFrameRate(Element element) => element.FindHierarchicalParent<Project>().GetFrameRate();
}

// A duration that stands in for one transition side's own, so an edit can be previewed before it is
// committed.
internal readonly record struct TransitionDurationOverride(ClipTransition Transition, MediaTime Duration);

// The transition at one clip boundary. Outgoing is null for an enter transition with nothing before it,
// and Incoming is null for an exit transition with nothing after it.
internal readonly record struct TransitionBoundary(
    Element? Outgoing,
    Element? Incoming,
    ClipTransition Transition,
    TimeRange Region)
{
    // How far the transition has played at time, before its easing, which the compositor applies as
    // evaluated for that time.
    public float GetLinearProgress(MediaTime time)
    {
        float linear = (float)((time - Region.Start).Flicks / (double)Region.Duration.Flicks);
        return Math.Clamp(linear, 0, 1);
    }

    // Where the outgoing element is evaluated: its own time, or its last frame once the transition plays
    // past its end.
    public MediaTime GetOutgoingTime(MediaTime time)
    {
        if (Outgoing == null) return time;

        MediaTime last = ElementTransitions.GetLastFrameTime(Outgoing);
        return time > last ? last : time;
    }

    // Where the incoming element is evaluated: its own time, or its first frame while the transition plays
    // ahead of its start.
    public MediaTime GetIncomingTime(MediaTime time)
    {
        if (Incoming == null || time >= Incoming.Start.Value) return time;

        return Incoming.Start.Value;
    }
}
