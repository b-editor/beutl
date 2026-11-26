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
        Rational frameRate = scene.GetFrameRate();
        bool graphics = target == CompositionTarget.Graphics;
        if (graphics && range.Start.RoundToFrame(frameRate, MidpointRounding.ToPositiveInfinity) >= range.End)
        {
            missing.RemoveAll(obj => obj.GetCompositionTarget() == target);
            return;
        }

        HashSet<Element>? participants = graphics ? ElementTransitions.GetParticipants(scene) : null;
        var pass = new InputPass(scene, element, missing, target, participants);
        pass.InspectLocalInputs();
        if (!missing.Exists(obj => obj.GetCompositionTarget() == target)) return;

        int layer = element.ZIndex.Value;
        int lastInputLayer = GetLastInputLayer(element);
        if (lastInputLayer <= layer) return;

        MediaTime tolerance = graphics ? ElementTransitions.AdjacencyTolerance : MediaTime.Zero;
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

        // Sample the compositor's clip selection where the set of available inputs can change.
        var boundaries = new List<MediaTime> { range.Start, range.End };
        foreach (Element candidate in found)
        {
            if (!candidate.IsEnabled.Value || candidate.ZIndex.Value > lastInputLayer && !(graphics && candidate.HasTrackMatte)) continue;

            AddBoundary(candidate.Range.Start);
            AddBoundary(candidate.Range.End);
            if (participants?.Contains(candidate) != true) continue;

            AddBoundary(candidate.Range.Start - tolerance);
            AddBoundary(candidate.Range.End + tolerance);
            AddBoundary(ElementTransitions.GetLastFrameTime(candidate));
            if (ElementTransitions.GetBoundaryAtStart(candidate) is { } enter) AddTransition(enter);
            if (ElementTransitions.GetBoundaryAtEnd(candidate) is { } exit) AddTransition(exit);
        }

        boundaries.Sort();
        for (int i = 1; i < boundaries.Count; i++)
        {
            MediaTime start = boundaries[i - 1];
            MediaTime end = boundaries[i];
            if (start == end) continue;
            MediaTime time = graphics ? start.RoundToFrame(frameRate, MidpointRounding.ToPositiveInfinity) : start;
            if (time >= end) continue;

            pass.InspectInterval(time);
            if (!missing.Exists(obj => obj.GetCompositionTarget() == target)) return;
        }

        void AddBoundary(MediaTime time)
        {
            if (range.Contains(time)) boundaries.Add(time);
        }

        void AddTransition(TransitionBoundary boundary)
        {
            AddBoundary(boundary.Region.Start);
            AddBoundary(boundary.Region.End);
        }
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

    private readonly record struct InputScope(PooledList<Element> Elements, MediaTime Time, bool IsMatteSource,
        MediaTime? FrameTime = null, MediaTime? SideTime = null);

    private readonly record struct TransitionRead(Element[] Offered, Element[] Blocked, Element[] Remaining);

    private sealed class InputPass(Scene scene, Element owner, List<EngineObject> missing,
        CompositionTarget target, HashSet<Element>? participants)
    {
        private readonly HashSet<int> _matteLayers = [];
        private readonly HashSet<Element> _evaluating = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<(PortalObject Portal, MediaTime Time, MediaTime? FrameTime, MediaTime? SideTime),
            (Element[] Elements, FlowInputKind Output)> _variablePortalOutputs = [];
        private readonly Dictionary<(TransitionBoundary Boundary, MediaTime Time, bool IsMatteSource),
            List<TransitionRead>> _transitionReads = [];
        private MediaTime _frameTime;

        public void InspectLocalInputs()
        {
            using var empty = new PooledList<Element>();
            ReadObjects(owner, new InputScope(empty, owner.Range.Start, IsMatteSource: false));
        }

        public void InspectInterval(MediaTime time)
        {
            _frameTime = time;
            _matteLayers.Clear();
            _variablePortalOutputs.Clear();
            _transitionReads.Clear();
            using var active = new PooledList<Element>();
            SceneCompositor.CollectFrameElements(scene, time, active, target, participants);
            InspectRoots(new InputScope(active, time, IsMatteSource: false));
        }

        private void InspectRoots(InputScope scope, int? layer = null)
        {
            for (int i = 0; i < scope.Elements.Count; i++)
            {
                Element element = scope.Elements[i];
                if (layer.HasValue && element.ZIndex.Value != layer.Value) continue;

                if (ReferenceEquals(element, owner) || SceneCompositor.HasPortal(element)
                    || (participants != null && (element.HasTrackMatte || participants.Contains(element))))
                {
                    Read(element, scope);
                    if (!missing.Exists(obj => obj.GetCompositionTarget() == target)) return;
                }
            }
        }

        private FlowInputKind Read(Element element, InputScope scope)
            => participants?.Contains(element) == true && ElementTransitions.TryGetActive(element, scope.Time, out var boundary)
                ? ReadTransition(boundary, scope)
                : ReadElement(element, scope);

        private FlowInputKind ReadElement(Element element, InputScope scope)
        {
            if (participants != null && !scope.IsMatteSource && !element.IsVisibleInOutput.Value) return FlowInputKind.None;
            if (!_evaluating.Add(element)) return FlowInputKind.None;
            try
            {
                FlowInputKind output = ReadObjects(element, scope);
                if (participants == null || !element.HasTrackMatte) return output;

                if (element.GetMatteSourceZIndex(scene, _frameTime) is { } layer) InspectMatte(layer);
                return FlowInputKind.Drawable;
            }
            finally
            {
                _evaluating.Remove(element);
            }
        }

        private void InspectMatte(int layer)
        {
            if (!_matteLayers.Add(layer)) return;

            using var elements = new PooledList<Element>();
            SceneCompositor.CollectFrameElements(scene, _frameTime, elements, target, participants);
            InspectRoots(new InputScope(elements, _frameTime, IsMatteSource: true), layer);
        }

        private FlowInputKind ReadObjects(Element element, InputScope scope)
        {
            bool isOwner = ReferenceEquals(element, owner);
            FlowInputKind flow = FlowInputKind.None;
            using var objects = new PooledList<EngineObject>();
            element.CollectObjects(target, objects);
            foreach (EngineObject obj in objects.Span)
            {
                if (obj is PortalObject portal)
                {
                    if (portal.Clear.Value && !portal.Clear.HasExpression) flow = FlowInputKind.None;
                    flow |= ReadPortal(portal, scope);
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

            return flow;
        }

        private FlowInputKind ReadPortal(PortalObject portal, InputScope scope)
        {
            var span = InputLayers(portal);
            if (!portal.Count.HasExpression) return TakePortalInputs(span, scope);

            // Count can let this Portal reach any prefix of the remaining lower layers, or none of
            // them. Inspect the longest prefix on a copy, leaving the caller's confirmed claims intact.
            ReadOnlySpan<Element> available = scope.Elements.Span;
            int first = 0;
            while (first < available.Length && available[first].ZIndex.Value < span.First) first++;
            available = available[first..];
            if (available.IsEmpty) return FlowInputKind.None;

            // A nested Portal can be reached repeatedly with the same remaining input order. Matte
            // reads also depend on the clips being evaluated by the caller, so they use a separate read.
            var key = (portal, scope.Time, scope.FrameTime, scope.SideTime);
            if (!scope.IsMatteSource && _variablePortalOutputs.TryGetValue(key, out var previous)
                && available.SequenceEqual(previous.Elements)) return previous.Output;

            using var possible = new PooledList<Element>(available.Length);
            possible.AddRange(available);
            FlowInputKind output = TakePortalInputs(span, scope with { Elements = possible });
            if (!scope.IsMatteSource) _variablePortalOutputs[key] = (available.ToArray(), output);
            return output;
        }

        private FlowInputKind TakePortalInputs((int First, int Last) span, InputScope scope)
        {
            FlowInputKind flow = FlowInputKind.None;
            int index = 0;
            while (PortalObject.TakeNextInput(scope.Elements, span, ref index) is { } element)
            {
                MediaTime time = SceneCompositor.GetPortalInputTime(element, scope.Time, scope.FrameTime, scope.SideTime);
                flow |= Read(element, scope with { Time = time });
            }

            return flow;
        }

        private FlowInputKind ReadTransition(TransitionBoundary boundary, InputScope scope)
        {
            if (!scope.IsMatteSource && !boundary.HasVisibleSide) return FlowInputKind.None;

            var key = (boundary, scope.Time, scope.IsMatteSource);
            // Matte reads can revisit a layer that is already being evaluated. Portal traversal within
            // a side only goes farther down, so earlier layers cannot block this read.
            int layer = (boundary.Outgoing ?? boundary.Incoming)!.ZIndex.Value;
            Element[] blocked = scope.IsMatteSource ? [.. _evaluating.Where(e => e.ZIndex.Value >= layer)] : [];
            if (!_transitionReads.TryGetValue(key, out var reads))
            {
                reads = [];
                _transitionReads.Add(key, reads);
            }

            foreach (TransitionRead read in reads)
            {
                if (!scope.Elements.Span.SequenceEqual(read.Offered) || !blocked.AsSpan().SequenceEqual(read.Blocked)) continue;
                if (read.Remaining.Length != scope.Elements.Count)
                {
                    scope.Elements.Clear();
                    scope.Elements.AddRange(read.Remaining);
                }

                return FlowInputKind.Drawable;
            }

            Element[] inputs = [.. scope.Elements];
            if (boundary.Outgoing is { } outgoing)
                ReadSide(outgoing, boundary.GetOutgoingTime(scope.Time), inputs, scope);
            if (boundary.Incoming is { } incoming)
                ReadSide(incoming, boundary.GetIncomingTime(scope.Time), inputs, scope);
            Element[] remaining = scope.Elements.Count == inputs.Length ? inputs : scope.Elements.Span.ToArray();
            reads.Add(new TransitionRead(inputs, blocked, remaining));
            return FlowInputKind.Drawable;
        }

        private void ReadSide(Element side, MediaTime time, Element[] inputs, InputScope scope)
        {
            if (!scope.IsMatteSource && !side.IsVisibleInOutput.Value) return;

            using var sideInputs = new PooledList<Element>(inputs.Length);
            bool usesFrameInputs = time == scope.Time || !SceneCompositor.HasPortal(side);
            bool mayUseFrameInputs = false;
            if (!usesFrameInputs)
            {
                foreach (EngineObject obj in side.Objects)
                {
                    if (obj is not PortalObject { IsEnabled.Value: true } portal
                        || !SceneCompositor.ReachesSharedFlowOperator(InputLayers(portal), inputs, time, scope.Time, participants!)) continue;
                    if (portal.Count.HasExpression) mayUseFrameInputs = true;
                    else usesFrameInputs = true;
                }
            }

            if (usesFrameInputs)
            {
                sideInputs.AddRange(inputs.AsSpan());
            }
            else
            {
                using var atSideTime = new PooledList<Element>();
                SceneCompositor.CollectFrameElements(scene, time, atSideTime, target, participants);
                SceneCompositor.CollectHeldSideInputs(scene.ElementIndex, time, scope.Time, inputs,
                    atSideTime, participants!, sideInputs);
            }

            var sideScope = new InputScope(sideInputs, time, scope.IsMatteSource, FrameTime: scope.Time, SideTime: time);
            using var offered = new PooledList<Element>(sideInputs.Count);
            offered.AddRange(sideInputs.Span);
            ReadElement(side, sideScope);

            if (!usesFrameInputs && mayUseFrameInputs)
            {
                // The expression can select either input list. Run each in its own order and carry
                // back only the claims present in both alternatives.
                using var frameInputs = new PooledList<Element>(inputs.Length);
                frameInputs.AddRange(inputs.AsSpan());
                ReadElement(side, sideScope with { Elements = frameInputs });
                foreach (Element input in frameInputs.Span)
                {
                    if (offered.Contains(input) && !sideInputs.Contains(input)) sideInputs.Add(input);
                }
            }

            SceneCompositor.RemoveConsumedSideInputs(scope.Elements, sideInputs, offered.Span);
        }
    }
}
