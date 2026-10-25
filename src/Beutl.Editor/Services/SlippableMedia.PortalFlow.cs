using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

internal static partial class SlippableMedia
{
    private sealed class PortalFlowSnapshot(Element[] candidates, HashSet<Element> active, Dictionary<Element, TransitionBoundary> transitions)
    {
        public IReadOnlyList<Element> Candidates => candidates;
        public IReadOnlySet<Element> Active => active;
        public IReadOnlyDictionary<Element, TransitionBoundary> Transitions => transitions;

        public bool Matches(PortalFlowSnapshot other)
            => active.SetEquals(other.Active) && transitions.Count == other.Transitions.Count
                && transitions.All(entry => other.Transitions.TryGetValue(entry.Key, out TransitionBoundary boundary)
                    && entry.Value.Transition == boundary.Transition && entry.Value.Outgoing == boundary.Outgoing && entry.Value.Incoming == boundary.Incoming);

        public static PortalFlowSnapshot Create(Element[] candidates, IReadOnlyDictionary<Element, TimeRange> ranges,
            TransitionBoundary[] boundaries, MediaTime time)
        {
            var active = ranges.Where(entry => entry.Value.Contains(time)).Select(entry => entry.Key).ToHashSet();
            var transitions = new Dictionary<Element, TransitionBoundary>();
            foreach (TransitionBoundary boundary in boundaries)
            {
                if (!boundary.Region.Contains(time)) continue;
                AddParticipant(boundary.Outgoing);
                AddParticipant(boundary.Incoming);

                void AddParticipant(Element? participant)
                {
                    if (participant == null || !ranges.TryGetValue(participant, out TimeRange range)) return;
                    // CoversTime lets a transition bridge the small tolerated gap.
                    if (time.Flicks < (Int128)range.Start.Flicks - ElementTransitions.AdjacencyTolerance.Flicks
                        || time.Flicks >= (Int128)range.End.Flicks + ElementTransitions.AdjacencyTolerance.Flicks) return;
                    active.Add(participant);
                    transitions[participant] = boundary;
                }
            }

            // Match SceneCompositor.KeepOneElementPerTransition, including the
            // outgoing clip staying hidden after a shortened overlap transition.
            Element[] ordered = candidates.Reverse().OrderBy(element => element.ZIndex.Value).ToArray();
            foreach (Element element in ordered.Reverse())
                if (active.Contains(element) && boundaries.Any(boundary => boundary.Outgoing == element
                    && boundary.Incoming is { } next && time >= boundary.Region.End && active.Contains(next)))
                    active.Remove(element);
            foreach (Element element in ordered)
            {
                if (!active.Contains(element) || !transitions.TryGetValue(element, out TransitionBoundary boundary)) continue;
                Element? partner = boundary.Outgoing == element ? boundary.Incoming : boundary.Outgoing;
                if (partner != null) active.Remove(partner);
            }
            foreach (Element element in transitions.Keys.Where(element => !active.Contains(element)).ToArray())
                transitions.Remove(element);
            return new(candidates, active, transitions);
        }
    }
}
