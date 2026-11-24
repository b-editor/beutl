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
            MediaTime time = graphics ? FirstFrameAtOrAfter(start, frameRate) : start;
            if (time >= end) continue;

            pass.InspectInterval(time, lastInputLayer);
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

    private readonly record struct InputScope(PooledList<Element> Elements, MediaTime Time, bool IsMatteSource,
        bool MayShareInputs, MediaTime? FrameTime = null, MediaTime? SideTime = null);

    private sealed class InputPass(Scene scene, Element owner, List<EngineObject> missing,
        CompositionTarget target, HashSet<Element>? participants)
    {
        private readonly HashSet<int> _matteLayers = [];
        private readonly HashSet<Element> _evaluating = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<(Element, InputScope), FlowInputKind> _possibleOutputs = [];
        private MediaTime _frameTime;
        private int _lastInputLayer;

        public void InspectLocalInputs()
        {
            using var empty = new PooledList<Element>();
            ReadObjects(owner, new InputScope(empty, owner.Range.Start, false, false));
        }

        public void InspectInterval(MediaTime time, int lastInputLayer)
        {
            _frameTime = time;
            _lastInputLayer = lastInputLayer;
            _matteLayers.Clear();
            _possibleOutputs.Clear();
            using var active = new PooledList<Element>();
            SceneCompositor.CollectFrameElements(scene, time, active, target, participants);
            InspectRoots(CreateScope(active, time, isMatteSource: false));

            // A hidden clip can be authored as a matte source. Its inputs use their own pass.
            if (participants != null && !owner.IsVisibleInOutput.Value) InspectMatte(owner.ZIndex.Value);
        }

        private InputScope CreateScope(PooledList<Element> elements, MediaTime time, bool isMatteSource)
        {
            // An expression-driven Count can change which Portal took a clip. Keep those possible
            // sources available for this pass while still using the pass's visibility rules.
            bool mayShare = elements.Any(element => element.ZIndex.Value < _lastInputLayer
                && (participants == null || isMatteSource || element.IsVisibleInOutput.Value)
                && HasVariablePortal(element));
            return new InputScope(elements, time, isMatteSource, mayShare);
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
        {
            var key = (element, scope);
            if (scope.MayShareInputs && _possibleOutputs.TryGetValue(key, out FlowInputKind output)) return output;

            output = participants?.Contains(element) == true && ElementTransitions.TryGetActive(element, scope.Time, out var boundary)
                ? ReadTransition(boundary, scope)
                : ReadElement(element, scope);
            if (scope.MayShareInputs) _possibleOutputs[key] = output;
            return output;
        }

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
            InspectRoots(CreateScope(elements, _frameTime, isMatteSource: true), layer);
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
            FlowInputKind flow = FlowInputKind.None;
            if (!scope.MayShareInputs)
            {
                int index = 0;
                while (PortalObject.TakeNextInput(scope.Elements, span, ref index) is { } element)
                    flow |= ReadInput(element);
                return flow;
            }

            foreach (Element element in scope.Elements.Span)
            {
                int layer = element.ZIndex.Value;
                if (layer > span.Last) break;
                if (layer >= span.First) flow |= ReadInput(element);
            }

            return flow;

            FlowInputKind ReadInput(Element element)
            {
                MediaTime time = SceneCompositor.GetPortalInputTime(element, scope.Time, scope.FrameTime, scope.SideTime);
                return Read(element, scope with { Time = time });
            }
        }

        private FlowInputKind ReadTransition(TransitionBoundary boundary, InputScope scope)
        {
            if (!scope.IsMatteSource && !boundary.HasVisibleSide) return FlowInputKind.None;

            Element[] inputs = [.. scope.Elements];
            if (boundary.Outgoing is { } outgoing)
                ReadSide(outgoing, boundary.GetOutgoingTime(scope.Time), inputs, scope);
            if (boundary.Incoming is { } incoming)
                ReadSide(incoming, boundary.GetIncomingTime(scope.Time), inputs, scope);
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

                if (mayUseFrameInputs)
                {
                    foreach (Element input in inputs)
                    {
                        if (!sideInputs.Contains(input)) sideInputs.Add(input);
                    }
                    scene.ElementIndex.Sort(sideInputs);
                }
            }

            InputScope sideScope = CreateScope(sideInputs, time, scope.IsMatteSource);
            sideScope = sideScope with
            {
                MayShareInputs = sideScope.MayShareInputs || scope.MayShareInputs || HasVariablePortal(side),
                FrameTime = scope.Time,
                SideTime = time,
            };
            using var offered = new PooledList<Element>(sideInputs.Count);
            offered.AddRange(sideInputs.Span);
            ReadElement(side, sideScope);
            SceneCompositor.RemoveConsumedSideInputs(scope.Elements, sideInputs, offered.Span);
        }

        private static bool HasVariablePortal(Element element)
            => element.Objects.Any(obj => obj is PortalObject { IsEnabled.Value: true, Count.HasExpression: true });
    }
}
