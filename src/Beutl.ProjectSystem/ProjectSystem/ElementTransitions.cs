using Beutl.Graphics.Transitions;
using Beutl.Media;

namespace Beutl.ProjectSystem;

// Resolves the transitions at clip boundaries: which elements they blend, over which span, and how far
// they have played at a given time.
internal static class ElementTransitions
{
    // Clip boundaries snap to frames, but a start and a length rounded separately can leave one element's
    // end a tick off the next element's start. Anything closer than this is one boundary.
    internal static readonly TimeSpan AdjacencyTolerance = TimeSpan.FromMilliseconds(1);

    private const int DefaultFrameRate = 30;

    // Whether outgoing and incoming meet at a boundary: enabled, on the same layer, incoming starting
    // after outgoing and no later than its end, and outgoing not reaching past incoming's end.
    public static bool AreAdjacent(Element outgoing, Element incoming)
    {
        return outgoing != incoming
               && outgoing.IsEnabled
               && incoming.IsEnabled
               && outgoing.ZIndex == incoming.ZIndex
               && outgoing.Start < incoming.Start
               && incoming.Start <= outgoing.Range.End + AdjacencyTolerance
               && outgoing.Range.End <= incoming.Range.End + AdjacencyTolerance;
    }

    // The element whose end meets element's start. When several do, the one that ends last wins, since
    // it is the one visible just before the boundary.
    public static Element? FindPrevious(Element element)
    {
        if (element.HierarchicalParent is not Scene scene) return null;

        Element? previous = null;
        foreach (Element item in scene.Children.GetMarshal().Value)
        {
            if (AreAdjacent(item, element) && (previous == null || item.Range.End > previous.Range.End))
            {
                previous = item;
            }
        }

        return previous;
    }

    // The element whose start meets element's end. When several do, the one that starts first wins.
    public static Element? FindNext(Element element)
    {
        if (element.HierarchicalParent is not Scene scene) return null;

        Element? next = null;
        foreach (Element item in scene.Children.GetMarshal().Value)
        {
            if (AreAdjacent(element, item) && (next == null || item.Start < next.Start))
            {
                next = item;
            }
        }

        return next;
    }

    // The transition at element's start, shared with the element before it when there is one.
    public static TransitionBoundary? GetBoundaryAtStart(Element element, TransitionDurationOverride? durationOverride = null)
    {
        if (!element.IsEnabled) return null;

        return CreateBoundary(FindPrevious(element), element, durationOverride);
    }

    // The transition at element's end, shared with the element after it when there is one.
    public static TransitionBoundary? GetBoundaryAtEnd(Element element, TransitionDurationOverride? durationOverride = null)
    {
        if (!element.IsEnabled) return null;

        return CreateBoundary(element, FindNext(element), durationOverride);
    }

    // The boundary transition element takes part in at time, if any. Spans never overlap within an
    // element, so there is at most one.
    public static bool TryGetActive(Element element, TimeSpan time, out TransitionBoundary boundary)
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
        ReadOnlySpan<Element> children = scene.Children.GetMarshal().Value;
        foreach (Element element in children)
        {
            bool enter = IsActive(element.EnterTransition);
            bool exit = IsActive(element.ExitTransition);
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
        }

        return regions;
    }

    // The time of the last frame the timeline shows inside element, so a held frame matches the one the
    // viewer saw right before the boundary instead of the one past the out point.
    public static TimeSpan GetLastFrameTime(Element element)
    {
        TimeSpan frame = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / GetFrameRate(element));
        TimeSpan time = element.Range.End - frame;
        return time < element.Start ? element.Start : time;
    }

    private static bool Touches(TimeRange a, TimeRange b)
    {
        return a.Start <= b.End && b.Start <= a.End;
    }

    private static bool IsActive(ClipTransition? transition) => transition is { IsEnabled: true };

    // An element's end counts as transitioning when it sets an exit transition or the element after it
    // sets an enter transition; likewise for its start.
    private static bool HasStartTransition(Element element)
    {
        return IsActive(element.EnterTransition) || IsActive(FindPrevious(element)?.ExitTransition);
    }

    private static bool HasEndTransition(Element element)
    {
        return IsActive(element.ExitTransition) || IsActive(FindNext(element)?.EnterTransition);
    }

    private static TimeSpan GetDuration(ClipTransition? transition, TransitionDurationOverride? durationOverride)
    {
        if (!IsActive(transition)) return TimeSpan.Zero;

        TimeSpan duration = durationOverride is { } value && ReferenceEquals(value.Transition, transition)
            ? value.Duration
            : transition!.Duration.CurrentValue;
        return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
    }

    private static TransitionBoundary? CreateBoundary(
        Element? outgoing, Element? incoming, TransitionDurationOverride? durationOverride)
    {
        ClipTransition? exit = outgoing?.ExitTransition;
        ClipTransition? enter = incoming?.EnterTransition;
        // The incoming side decides how the two blend; the outgoing side then only adds its duration.
        ClipTransition? transition = IsActive(enter) ? enter : IsActive(exit) ? exit : null;
        if (transition == null) return null;

        TimeSpan start;
        TimeSpan end;
        if (outgoing != null && incoming != null)
        {
            // Centred on the cut when both sides add the same duration, ahead of or behind it when only
            // one does, and covering the overlap when the two elements overlap.
            start = Min(outgoing.Range.End - GetDuration(exit, durationOverride), incoming.Start);
            end = Max(incoming.Start + GetDuration(enter, durationOverride), outgoing.Range.End);
        }
        else if (outgoing != null)
        {
            start = outgoing.Range.End - GetDuration(exit, durationOverride);
            end = outgoing.Range.End;
        }
        else
        {
            start = incoming!.Start;
            end = incoming.Start + GetDuration(enter, durationOverride);
        }

        // A span never leaves its elements, and stops at the middle of an element that also transitions
        // at its other end, so the spans at an element's two ends never overlap.
        if (outgoing != null)
        {
            start = Max(start, outgoing.Start);
            if (HasStartTransition(outgoing))
            {
                start = Max(start, outgoing.Start + (outgoing.Length / 2));
            }
        }

        if (incoming != null)
        {
            end = Min(end, incoming.Range.End);
            if (HasEndTransition(incoming))
            {
                end = Min(end, incoming.Start + (incoming.Length / 2));
            }
        }

        if (end <= start) return null;

        return new TransitionBoundary(outgoing, incoming, transition, TimeRange.FromRange(start, end));
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private static int GetFrameRate(Element element)
    {
        Project? project = element.FindHierarchicalParent<Project>();
        if (project != null
            && project.Variables.TryGetValue(ProjectVariableKeys.FrameRate, out string? value)
            && int.TryParse(value, out int rate)
            && rate > 0)
        {
            return rate;
        }

        return DefaultFrameRate;
    }
}

// A duration that stands in for one transition side's own, so an edit can be previewed before it is
// committed.
internal readonly record struct TransitionDurationOverride(ClipTransition Transition, TimeSpan Duration);

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
    public float GetLinearProgress(TimeSpan time)
    {
        float linear = (float)((time - Region.Start).Ticks / (double)Region.Duration.Ticks);
        return Math.Clamp(linear, 0, 1);
    }

    // Where the outgoing element is evaluated: its own time, or its last frame once the transition plays
    // past its end.
    public TimeSpan GetOutgoingTime(TimeSpan time)
    {
        if (Outgoing == null) return time;

        TimeSpan last = ElementTransitions.GetLastFrameTime(Outgoing);
        return time > last ? last : time;
    }

    // Where the incoming element is evaluated: its own time, or its first frame while the transition plays
    // ahead of its start.
    public TimeSpan GetIncomingTime(TimeSpan time)
    {
        if (Incoming == null || time >= Incoming.Start) return time;

        return Incoming.Start;
    }
}
