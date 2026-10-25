using Beutl.Graphics.Transitions;
using Beutl.Media;

namespace Beutl.ProjectSystem;

// Resolves the transitions at clip boundaries: which elements they blend, over which span, and how far
// they have played at a given time.
internal static class ElementTransitions
{
    // Clip boundaries snap to frames, but a time typed by hand or read outside a project can leave one element's
    // end just off the next element's start. Anything closer than this is one boundary.
    internal static readonly MediaTime AdjacencyTolerance = MediaTime.FromSeconds(0.001);

    // Whether outgoing and incoming meet at a boundary: enabled, on the same layer, incoming starting
    // after outgoing and no later than its end, and outgoing not reaching past incoming's end.
    public static bool AreAdjacent(Element outgoing, Element incoming, IReadOnlyDictionary<Element, TimeRange>? ranges = null)
    {
        TimeRange from = GetRange(outgoing, ranges);
        TimeRange to = GetRange(incoming, ranges);
        return outgoing != incoming
               && outgoing.IsEnabled.Value
               && incoming.IsEnabled.Value
               && outgoing.ZIndex.Value == incoming.ZIndex.Value
               && from.Start < to.Start
               && to.Start.Flicks <= (Int128)from.End.Flicks + AdjacencyTolerance.Flicks
               && from.End.Flicks <= (Int128)to.End.Flicks + AdjacencyTolerance.Flicks;
    }

    // The element whose end meets element's start and that meets no other element's start first, so each
    // boundary pairs two elements that choose each other. When several meet element's start, the one that
    // ends last wins, since it is the one visible just before the boundary.
    public static Element? FindPrevious(Element element, IReadOnlyDictionary<Element, TimeRange>? ranges = null)
    {
        Element? previous = FindPreviousCandidate(element, ranges);
        return previous != null && FindNextCandidate(previous, ranges) == element ? previous : null;
    }

    // The element whose start meets element's end and that no other element meets first; when several
    // start there, the one that starts first wins.
    public static Element? FindNext(Element element, IReadOnlyDictionary<Element, TimeRange>? ranges = null)
    {
        Element? next = FindNextCandidate(element, ranges);
        return next != null && FindPreviousCandidate(next, ranges) == element ? next : null;
    }

    private static Element? FindPreviousCandidate(Element element, IReadOnlyDictionary<Element, TimeRange>? ranges)
    {
        if (element.HierarchicalParent is not Scene scene) return null;

        Element? previous = null;
        foreach (Element item in ranges?.Keys ?? scene.Children)
        {
            if (AreAdjacent(item, element, ranges) && (previous == null || GetRange(item, ranges).End > GetRange(previous, ranges).End))
            {
                previous = item;
            }
        }

        return previous;
    }

    private static Element? FindNextCandidate(Element element, IReadOnlyDictionary<Element, TimeRange>? ranges)
    {
        if (element.HierarchicalParent is not Scene scene) return null;

        Element? next = null;
        foreach (Element item in ranges?.Keys ?? scene.Children)
        {
            if (AreAdjacent(element, item, ranges) && (next == null || GetRange(item, ranges).Start < GetRange(next, ranges).Start))
            {
                next = item;
            }
        }

        return next;
    }

    // The transition at element's start, shared with the element before it when there is one.
    public static TransitionBoundary? GetBoundaryAtStart(Element element, TransitionDurationOverride? durationOverride = null,
        IReadOnlyDictionary<Element, TimeRange>? ranges = null)
    {
        if (!element.IsEnabled.Value) return null;

        return CreateBoundary(FindPrevious(element, ranges), element, durationOverride, ranges);
    }

    // The transition at element's end, shared with the element after it when there is one.
    public static TransitionBoundary? GetBoundaryAtEnd(Element element, TransitionDurationOverride? durationOverride = null,
        IReadOnlyDictionary<Element, TimeRange>? ranges = null)
    {
        if (!element.IsEnabled.Value) return null;

        return CreateBoundary(element, FindNext(element, ranges), durationOverride, ranges);
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
    {
        HashSet<Element>? participants = null;
        ReadOnlySpan<Element> children = scene.Children.AsSpan();
        foreach (Element element in children)
        {
            bool enter = IsActive(element.EnterTransition.Value);
            bool exit = IsActive(element.ExitTransition.Value);
            if (!enter && !exit) continue;

            participants ??= [];
            participants.Add(element);
            foreach (Element other in children)
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
    public static List<TimeRange> GetRegionsNear(Scene scene, TimeRange range)
    {
        var regions = new List<TimeRange>();
        foreach (Element element in GetParticipants(scene))
        {
            if (GetBoundaryAtStart(element) is { } start && Touches(start.Region, range))
            {
                regions.Add(start.Region);
            }

            // A boundary with an element after it is found again from that element's start.
            if (GetBoundaryAtEnd(element) is { Incoming: null } end && Touches(end.Region, range))
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

        return regions;
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
    private static bool HasStartTransition(Element element, TransitionDurationOverride? durationOverride,
        IReadOnlyDictionary<Element, TimeRange>? ranges = null)
    {
        Element? previous = FindPrevious(element, ranges);
        return HasSpan(previous, element, previous?.ExitTransition.Value, element.EnterTransition.Value, durationOverride, ranges);
    }

    private static bool HasEndTransition(Element element, TransitionDurationOverride? durationOverride,
        IReadOnlyDictionary<Element, TimeRange>? ranges = null)
    {
        Element? next = FindNext(element, ranges);
        return HasSpan(element, next, element.ExitTransition.Value, next?.EnterTransition.Value, durationOverride, ranges);
    }

    private static bool HasSpan(
        Element? outgoing,
        Element? incoming,
        ClipTransition? exit,
        ClipTransition? enter,
        TransitionDurationOverride? durationOverride,
        IReadOnlyDictionary<Element, TimeRange>? ranges)
    {
        if (!IsActive(exit) && !IsActive(enter)) return false;

        return GetDuration(exit, durationOverride) > MediaTime.Zero
               || GetDuration(enter, durationOverride) > MediaTime.Zero
               || (outgoing != null && incoming != null && GetRange(incoming, ranges).Start < GetRange(outgoing, ranges).End);
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
        Element? outgoing, Element? incoming, TransitionDurationOverride? durationOverride, IReadOnlyDictionary<Element, TimeRange>? ranges)
    {
        ClipTransition? exit = outgoing?.ExitTransition.Value;
        ClipTransition? enter = incoming?.EnterTransition.Value;
        // The incoming side decides how the two blend; the outgoing side then only adds its duration.
        ClipTransition? transition = IsActive(enter) ? enter : IsActive(exit) ? exit : null;
        if (transition == null) return null;

        TimeRange from = outgoing != null ? GetRange(outgoing, ranges) : default;
        TimeRange to = incoming != null ? GetRange(incoming, ranges) : default;
        // Bound the additions before evaluating them, including maximum-duration
        // resize probes. The span is clipped to these same ranges below.
        MediaTime exitDuration = Min(GetDuration(exit, durationOverride), from.Duration);
        MediaTime enterDuration = Min(GetDuration(enter, durationOverride), to.Duration);
        MediaTime start;
        MediaTime end;
        if (outgoing != null && incoming != null)
        {
            // Centred on the cut when both sides add the same duration, ahead of or behind it when only
            // one does, and covering the overlap when the two elements overlap.
            start = Min(from.End - exitDuration, to.Start);
            end = Max(to.Start + enterDuration, from.End);
        }
        else if (outgoing != null)
        {
            start = from.End - exitDuration;
            end = from.End;
        }
        else
        {
            start = to.Start;
            end = to.Start + enterDuration;
        }

        // A span never leaves its elements, and stops at the middle of an element that also transitions
        // at its other end, so the spans at an element's two ends never overlap.
        if (outgoing != null)
        {
            start = Max(start, from.Start);
            if (HasStartTransition(outgoing, durationOverride, ranges))
            {
                start = Max(start, from.Start + new MediaTime(from.Duration.Flicks / 2));
            }
        }

        if (incoming != null)
        {
            end = Min(end, to.End);
            if (HasEndTransition(incoming, durationOverride, ranges))
            {
                end = Min(end, to.Start + new MediaTime(to.Duration.Flicks / 2));
            }
        }

        if (end <= start) return null;

        return new TransitionBoundary(outgoing, incoming, transition, TimeRange.FromRange(start, end));
    }

    private static MediaTime Min(MediaTime a, MediaTime b) => a < b ? a : b;

    // Editors can resolve the same transition against proposed geometry without
    // mutating the scene. Keys include all possible neighbours on relevant layers.
    private static TimeRange GetRange(Element element, IReadOnlyDictionary<Element, TimeRange>? ranges)
        => ranges != null && ranges.TryGetValue(element, out TimeRange range) ? range : element.Range;

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
