using Beutl.Audio;
using Beutl.Collections.Pooled;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Lighting;
using Beutl.Media;

namespace Beutl.ProjectSystem;

internal static class FlowInputDiagnostics
{
    public static IReadOnlyList<EngineObject> FindMissingInputs(Scene scene, Element element)
    {
        if (!element.IsEnabled.Value || element.Range.IsEmpty) return [];

        List<EngineObject>? missing = null;
        foreach (EngineObject obj in element.Objects)
        {
            if (obj is IFlowInputOperator { HasNestedInput: false } && obj.IsEnabled.Value)
                (missing ??= []).Add(obj);
        }

        if (missing is null) return [];

        Analyze(scene, element, missing, CompositionTarget.Graphics);
        Analyze(scene, element, missing, CompositionTarget.Audio);
        return missing;
    }

    private static void Analyze(Scene scene, Element element, List<EngineObject> missing, CompositionTarget target)
    {
        if (!missing.Exists(obj => obj.GetCompositionTarget() == target)) return;

        TimeRange range = element.Range;
        Rational frameRate = scene.FindHierarchicalParent<Project>().GetFrameRate();
        bool graphics = target == CompositionTarget.Graphics;
        if (graphics && FirstFrameAtOrAfter(range.Start, frameRate) >= range.End)
        {
            missing.RemoveAll(obj => obj.GetCompositionTarget() == target);
            return;
        }

        using var active = new PooledList<Element>();
        var pass = new InputPass(scene, element, missing, target, active);
        pass.Read(element);
        if (!missing.Exists(obj => obj.GetCompositionTarget() == target)) return;

        int layer = element.ZIndex.Value;
        int lastInputLayer = GetLastInputLayer(element);
        if (lastInputLayer <= layer) return;

        HashSet<Element>? participants = graphics && scene.HasTransitions ? ElementTransitions.GetParticipants(scene) : null;
        MediaTime tolerance = participants is null ? MediaTime.Zero : ElementTransitions.AdjacencyTolerance;
        SceneElementIndex index = scene.ElementIndex;
        using var found = new PooledList<Element>();
        index.Query(range.Start - tolerance, range.End + tolerance, found);
        index.Sort(found);

        // A clip in the portal's span can itself take a layer farther down.
        bool hasCandidate = false;
        foreach (Element candidate in found.Span)
        {
            int candidateLayer = candidate.ZIndex.Value;
            if (candidateLayer > lastInputLayer) break;
            if (candidateLayer <= layer || !candidate.IsEnabled.Value) continue;
            hasCandidate = true;
            lastInputLayer = Math.Max(lastInputLayer, GetLastInputLayer(candidate));
        }
        if (!hasCandidate) return;

        // For constant portal settings, the set and order of inputs can only change at these boundaries.
        var boundaries = new List<MediaTime> { range.Start, range.End };
        var candidates = new List<Candidate>(found.Count);
        var membership = new List<MembershipChange>();
        foreach (Element candidate in found)
        {
            if (!candidate.IsEnabled.Value || (candidate.Objects.Count == 0 && participants?.Contains(candidate) != true)) continue;
            if (candidate.ZIndex.Value > lastInputLayer && !(graphics && candidate.HasTrackMatte)) continue;

            bool hasTransition = participants?.Contains(candidate) == true;
            TimeRange enter = hasTransition ? ElementTransitions.GetBoundaryAtStart(candidate)?.Region ?? default : default;
            TimeRange exit = hasTransition ? ElementTransitions.GetBoundaryAtEnd(candidate)?.Region ?? default : default;
            TimeRange availability = candidate.Range.Union(enter).Union(exit).Intersect(range);
            if (availability.IsEmpty) continue;

            int candidateIndex = candidates.Count;
            bool hasVariablePortal = candidate.Objects.Any(obj => obj is PortalObject { IsEnabled.Value: true, Count.HasExpression: true });
            candidates.Add(new Candidate(candidate, enter, exit, hasVariablePortal));
            membership.Add(new MembershipChange(availability.Start, candidateIndex, IsEntering: true));
            membership.Add(new MembershipChange(availability.End, candidateIndex, IsEntering: false));
            AddBoundaries(candidate.Range);
            AddBoundaries(enter);
            AddBoundaries(exit);
        }

        boundaries.Sort();
        membership.Sort(static (a, b) => a.Time.CompareTo(b.Time));
        var current = new SortedSet<int>();
        int nextChange = 0;
        for (int i = 1; i < boundaries.Count; i++)
        {
            MediaTime start = boundaries[i - 1];
            MediaTime end = boundaries[i];
            if (start == end) continue;
            MediaTime time = graphics ? FirstFrameAtOrAfter(start, frameRate) : start;
            if (time >= end) continue;

            // Walk only the clips present in this interval, even for a portal that spans a long scene.
            while (nextChange < membership.Count && membership[nextChange].Time <= time)
            {
                MembershipChange change = membership[nextChange++];
                if (change.IsEntering) current.Add(change.CandidateIndex);
                else current.Remove(change.CandidateIndex);
            }

            pass.InspectInterval(candidates, current, time);
            if (!missing.Exists(obj => obj.GetCompositionTarget() == target)) return;
        }

        void AddBoundaries(TimeRange input)
        {
            if (!input.Intersects(range)) return;
            if (range.Contains(input.Start)) boundaries.Add(input.Start);
            if (range.Contains(input.End)) boundaries.Add(input.End);
        }
    }

    private static MediaTime FirstFrameAtOrAfter(MediaTime time, Rational rate)
    {
        long frame = time.ToFrame(rate, MidpointRounding.AwayFromZero);
        MediaTime frameTime = MediaTime.FromFrames(frame, rate);
        return frameTime >= time ? frameTime : MediaTime.FromFrames(frame + 1, rate);
    }

    private static (int First, int Last) InputLayers(PortalObject portal)
        => portal.Count.HasExpression
            ? (portal.ZIndex.Value + 1, int.MaxValue)
            : portal.GetInputLayerSpan(portal.Count.Value);

    private static int GetLastInputLayer(Element element)
    {
        int lastLayer = element.ZIndex.Value;
        foreach (EngineObject obj in element.Objects)
        {
            if (obj is PortalObject { IsEnabled.Value: true } portal)
                lastLayer = Math.Max(lastLayer, InputLayers(portal).Last);
        }

        return lastLayer;
    }

    private static FlowInputKind OutputKind(EngineObject obj) => obj switch
    {
        Drawable => FlowInputKind.Drawable,
        Object3D => FlowInputKind.Object3D,
        Light3D => FlowInputKind.Light3D,
        Sound => FlowInputKind.Sound,
        _ => FlowInputKind.None
    };

    private readonly record struct Candidate(Element Element, TimeRange Enter, TimeRange Exit, bool HasVariablePortal);

    private readonly record struct MembershipChange(MediaTime Time, int CandidateIndex, bool IsEntering);

    private sealed class InputPass(Scene scene, Element owner, List<EngineObject> missing,
        CompositionTarget target, PooledList<Element> active)
    {
        private readonly HashSet<Element> _transitioning = [];
        private readonly Dictionary<Element, FlowInputKind> _possibleOutputs = [];
        private bool _mayShareInputs;
        private bool _visitedOwner;

        public void InspectInterval(List<Candidate> candidates, SortedSet<int> current, MediaTime time)
        {
            active.Clear();
            _transitioning.Clear();
            _possibleOutputs.Clear();
            _visitedOwner = false;
            _mayShareInputs = target == CompositionTarget.Graphics && !owner.IsVisibleInOutput.Value;
            foreach (int index in current)
            {
                Candidate candidate = candidates[index];
                Element element = candidate.Element;
                bool transition = candidate.Enter.Contains(time) || candidate.Exit.Contains(time);
                if (!transition && !element.Range.Contains(time)) continue;
                if (transition) _transitioning.Add(element);
                active.Add(element);
                // These paths can change which evaluation owns a clip's inputs. Keep the possible sources
                // available to every portal in this interval so they cannot produce a false warning.
                _mayShareInputs |= candidate.HasVariablePortal || transition
                    || (target == CompositionTarget.Graphics && element.HasTrackMatte);
            }

            if (_mayShareInputs)
            {
                Read(owner);
                return;
            }

            for (int i = 0; i < active.Count && !_visitedOwner; i++)
            {
                Element element = active[i];
                // Earlier roots can affect the owner by claiming clips through a portal.
                if (ReferenceEquals(element, owner) || element.Objects.Any(obj => obj is PortalObject { IsEnabled.Value: true }))
                    Read(element);
            }
        }

        public FlowInputKind Read(Element element)
        {
            bool isOwner = ReferenceEquals(element, owner);
            bool transition = _transitioning.Contains(element);
            if (!isOwner && target == CompositionTarget.Graphics && !element.IsVisibleInOutput.Value && !transition && !_mayShareInputs)
                return FlowInputKind.None;
            if (_mayShareInputs && _possibleOutputs.TryGetValue(element, out FlowInputKind output)) return output;

            _visitedOwner |= isOwner;
            FlowInputKind flow = FlowInputKind.None;
            using var objects = new PooledList<EngineObject>();
            element.CollectObjects(target, objects);
            foreach (EngineObject obj in objects.Span)
            {
                if (obj is PortalObject portal)
                {
                    if (portal.Clear.Value && !portal.Clear.HasExpression) flow = FlowInputKind.None;
                    flow |= ReadPortal(element, portal, transition);
                }
                else if (obj is IFlowInputOperator input)
                {
                    if (isOwner && (flow & input.RequiredInput) != 0) missing.Remove(obj);

                    // These operators consume all matching inputs, except the time controller, whose
                    // replacement drawable preserves the presence of that kind either way.
                    flow &= ~input.ConsumedInputs;
                }
                else if (obj is IFlowOperator)
                {
                    flow |= FlowInputKind.Any;
                }

                flow |= OutputKind(obj);
            }

            if (target == CompositionTarget.Graphics && element.HasTrackMatte) flow = FlowInputKind.Drawable;
            if (transition) flow |= FlowInputKind.Drawable;
            if (_mayShareInputs) _possibleOutputs[element] = flow;
            return flow;
        }

        private FlowInputKind ReadPortal(Element owner, PortalObject portal, bool transition)
        {
            var span = InputLayers(portal);
            FlowInputKind flow = FlowInputKind.None;
            if (!_mayShareInputs)
            {
                int index = 0;
                while (PortalObject.TakeNextInput(active, span, ref index) is { } element)
                    flow |= Read(element);
                return flow;
            }

            foreach (Element element in active.Span)
            {
                int layer = element.ZIndex.Value;
                if (layer > span.Last) break;
                if (layer >= span.First) flow |= Read(element);
            }

            if (transition && span.First <= span.Last) flow |= ReadHeldInputs(owner, span);
            return flow;
        }

        // A transition can run the owner at its held edge, with sources outside the current interval.
        private FlowInputKind ReadHeldInputs(Element element, (int First, int Last) span)
        {
            using var candidates = new PooledList<Element>();
            SceneElementIndex index = scene.ElementIndex;
            var tolerance = ElementTransitions.AdjacencyTolerance;
            index.Query(element.Range.Start - tolerance, element.Range.End + tolerance, candidates);
            var participants = ElementTransitions.GetParticipants(scene);
            FlowInputKind possible = FlowInputKind.None;
            foreach (Element candidate in candidates.Span)
            {
                if (!candidate.IsEnabled.Value || candidate.ZIndex.Value < span.First || candidate.ZIndex.Value > span.Last) continue;
                if (candidate.HasTrackMatte || participants.Contains(candidate)) possible |= FlowInputKind.Drawable;
                foreach (EngineObject obj in candidate.Objects)
                {
                    if (!obj.IsEnabled.Value) continue;
                    if (obj is IFlowOperator and not IFlowInputOperator) return FlowInputKind.Any;
                    if (obj is PortalObject portal)
                    {
                        var layers = InputLayers(portal);
                        if (layers.First <= layers.Last) return FlowInputKind.Any;
                    }
                    possible |= OutputKind(obj) & ~FlowInputKind.Sound;
                }
            }

            return possible;
        }
    }
}
