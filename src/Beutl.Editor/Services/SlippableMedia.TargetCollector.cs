using Beutl.Audio;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Graphics.Transitions;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

internal static partial class SlippableMedia
{
    private sealed class TargetCollector
    {
        private readonly Element _element;
        private readonly IReadOnlySet<Element>? _timingPeers;
        private readonly bool _ignoreLoops;
        private readonly IReadOnlyDictionary<Element, MediaTimeMapping.TrimRole>? _timingRoles;
        private readonly IReadOnlySet<Element>? _portalCandidates;
        private readonly bool _includePortalInputs;
        private readonly PortalFlowSnapshot? _portalFlow;
        private readonly IReadOnlySet<DrawableTimeController>? _reenabledControllers;
        private readonly Dictionary<MediaSource, MediaTime?> _sourceDurations;
        private readonly List<Target> _targets = new();
        private int _unresolvedPortalTargets;
        private readonly List<DrawableTimeController> _disabledFlowControllers = new();
        private readonly HashSet<object> _path = new();
        private readonly List<MediaTimeMapping.ControllerLink> _controllers = new();
        private readonly Dictionary<int, TimelineLayer> _layers = new();
        private readonly bool _hasSolo;
        private readonly int _sampleRate;
        private readonly HashSet<Element> _consumedPortalElements = new();
        private bool _opaqueConsumption;

        public TargetCollector(Element element, IReadOnlySet<Element>? timingPeers, bool ignoreLoops,
            IReadOnlyDictionary<Element, MediaTimeMapping.TrimRole>? timingRoles, IReadOnlySet<Element>? portalCandidates, bool includePortalInputs = true,
            IReadOnlySet<DrawableTimeController>? reenabledControllers = null, Dictionary<MediaSource, MediaTime?>? sourceDurations = null,
            PortalFlowSnapshot? portalFlow = null)
        {
            _element = element;
            _timingPeers = timingPeers;
            _ignoreLoops = ignoreLoops;
            _timingRoles = timingRoles;
            _portalCandidates = portalCandidates;
            _includePortalInputs = includePortalInputs;
            _portalFlow = portalFlow;
            _reenabledControllers = reenabledControllers;
            _sourceDurations = sourceDurations ?? new(ReferenceEqualityComparer.Instance);
            if (element.HierarchicalParent is Scene layerScene)
                foreach (TimelineLayer layer in layerScene.Layers) _layers.TryAdd(layer.ZIndex.Value, layer);
            _hasSolo = _layers.Values.Any(layer => layer.IsSolo.Value);
            Project? project = element.FindHierarchicalParent<Project>();
            _sampleRate = project.GetSampleRate();
        }

        public List<Target> Collect()
        {
            foreach (Node node in BuildFlow()) CollectNode(node);
            // Imported sources do not define the portal's display interval. If
            // there is no local media, even an unknown input has no bounds to add.
            if (!_includePortalInputs && _targets.Count == _unresolvedPortalTargets) return [];
            if (_reenabledControllers != null || _disabledFlowControllers.Count == 0 || _targets.Any(t => !t.IsSupported)) return _targets;
            // A disabled controller contributes bounds only. Its future clock
            // must never replace the clock used to write the current source offset.
            // Rebuild the complete Flow: consuming its first entry moves a
            // controller to the end and can change a later controller's target.
            // Bound the state space on the editing thread; reject an unproven
            // graph atomically instead of omitting possible re-enabling states.
            const int maximumReenabledControllers = 7;
            if (_disabledFlowControllers.Count > maximumReenabledControllers)
            {
                CollectNode(new Node(new PortalObject(), [], Opaque: true));
                return _targets;
            }
            var targetsByOffset = _targets.ToLookup(t => t.Offset);
            for (int state = 1; state < 1 << _disabledFlowControllers.Count; state++)
            {
                var enabled = new HashSet<DrawableTimeController>();
                for (int i = 0; i < _disabledFlowControllers.Count; i++)
                    if ((state & (1 << i)) != 0) enabled.Add(_disabledFlowControllers[i]);
                var collector = new TargetCollector(_element, _timingPeers, _ignoreLoops, _timingRoles, _portalCandidates, _includePortalInputs, enabled, _sourceDurations, _portalFlow);
                foreach (Target reenabled in collector.Collect())
                {
                    Target[] owners = targetsByOffset[reenabled.Offset].ToArray();
                    if (owners.Length == 0)
                    {
                        // A newly opaque or previously absent input has no
                        // established offset clock. Preserve atomic rejection.
                        CollectNode(new Node(new PortalObject(), [], Opaque: true));
                        return _targets;
                    }
                    foreach (Target target in owners) target.SampleMappings.Add(reenabled.Mapping);
                }
            }
            return _targets;
        }

        private bool IsEnabled(EngineObject obj)
            => obj.IsEnabled.Value || obj is DrawableTimeController controller && _reenabledControllers?.Contains(controller) == true;

        public Element[] GetResizeCandidates(Scene scene)
        {
            PortalObject[] portals = _element.Objects.OfType<PortalObject>().Where(p => p.IsEnabled.Value).ToArray();
            Element[] providers = scene.Children.Where(child => child != _element && child.IsEnabled.Value
                && LayerVisible(child, CompositionTarget.Graphics)
                && portals.Any(p => p.Count.HasExpression || p.ZIndex.Value < child.ZIndex.Value
                    && (long)p.ZIndex.Value + p.Count.Value >= child.ZIndex.Value)).ToArray();
            var transitionLayers = providers.Where(child => child.EnterTransition.Value is { IsEnabled.Value: true }
                    || child.ExitTransition.Value is { IsEnabled.Value: true })
                .Select(child => child.ZIndex.Value).ToHashSet();
            // All neighbours on a transitioning layer affect pairing, even an
            // otherwise empty clip. Plain audio clips cannot affect graphic flow.
            var relevant = providers.Where(child => transitionLayers.Contains(child.ZIndex.Value)
                || child.Objects.Any(obj => obj.IsEnabled.Value && obj.GetCompositionTarget() != CompositionTarget.Audio)).ToHashSet();
            // An earlier portal can consume a provider before this element does.
            // Keep its edges, but not unrelated clips on those earlier layers.
            foreach (Element child in scene.Children)
                if (child != _element && child.IsEnabled.Value && child.ZIndex.Value <= _element.ZIndex.Value
                    && LayerVisible(child, CompositionTarget.Graphics)
                    && child.Objects.OfType<PortalObject>().Any(p => p.IsEnabled.Value
                        && (p.Count.HasExpression || providers.Any(provider => p.ZIndex.Value < provider.ZIndex.Value
                            && (long)p.ZIndex.Value + p.Count.Value >= provider.ZIndex.Value))))
                    relevant.Add(child);
            return scene.Children.Where(relevant.Contains).ToArray();
        }

        private MediaTime? GetSourceDuration(MediaSource? source)
        {
            if (source == null) return null;
            if (_sourceDurations.TryGetValue(source, out MediaTime? duration)) return duration;
            // All re-enabling states use the same pre-edit media. Keep only its
            // duration in this operation's cache so each reader is opened once
            // and its temporary resource is still disposed immediately.
            using var resource = source.ToResource(CompositionContext.Default);
            duration = resource switch
            {
                VideoSource.Resource video => video.Duration,
                SoundSource.Resource sound => sound.Duration > MediaTime.Zero ? sound.Duration : null,
                _ => null
            };
            _sourceDurations.Add(source, duration);
            return duration;
        }

        private List<Node> BuildFlow()
        {
            var flows = new List<List<Node>> { new() };
            bool portalFlow = false;
            foreach (EngineObject obj in _element.Objects)
            {
                if (obj is PortalObject { IsEnabled.Value: true } portal)
                {
                    portalFlow = true;
                    bool? clear = portal.Clear.Value;
                    if (portal.Clear.HasExpression)
                        clear = _includePortalInputs ? true
                            : portal.Clear.Expression is StringExpression<bool> expression && expression.TryGetConstantValue(out bool value) ? value : null;
                    if (clear == true) flows.Clear();
                    // Clear only removes the accumulated Flow, not consumed scene
                    // providers. An unknown result adds one cleared path alongside
                    // the retained paths; it must not erase their local media.
                    if (clear != false) flows.Add([]);
                    List<Node>? imported = _includePortalInputs && portal.Clear.HasExpression ? null : ResolvePortal(portal);
                    if (imported == null)
                    {
                        imported = [new Node(portal, [], Opaque: true, PortalInput: true)];
                        _opaqueConsumption = true;
                    }
                    foreach (List<Node> flow in flows)
                    {
                        flow.AddRange(imported);
                        flow.Add(new Node(portal, []));
                    }
                    continue;
                }
                foreach (List<Node> flow in flows)
                {
                    IReadOnlyList<Node> inputs = [];
                    if (obj is DrawableTimeController controller)
                    {
                        int index = flow.FindIndex(node => node.Opaque || node.Object is Drawable && IsEnabled(node.Object));
                        if (index >= 0)
                        {
                            Node input = flow[index];
                            if (IsEnabled(controller))
                            {
                                inputs = [input];
                                flow.RemoveAt(index);
                            }
                            else if (!_disabledFlowControllers.Contains(controller))
                            {
                                _disabledFlowControllers.Add(controller);
                            }
                        }
                    }
                    else if (obj.IsEnabled.Value && obj is SoundGroup or DrawableGroup or DrawableDecorator)
                    {
                        bool Consumes(Node node) => node.Opaque || IsEnabled(node.Object)
                            && (obj is SoundGroup ? node.Object is Sound : node.Object is Drawable);
                        inputs = flow.FindAll(Consumes);
                        flow.RemoveAll(Consumes);
                    }
                    flow.Add(new Node(obj, inputs, FlowResolved: portalFlow));
                }
            }
            return flows.SelectMany(flow => flow).Distinct().ToList();
        }

        private List<Node>? ResolvePortal(PortalObject portal)
        {
            if (portal.Count.HasExpression) return null;
            if (portal.Count.Value <= 0) return [];
            if (_opaqueConsumption || _element.HierarchicalParent is not Scene scene) return null;
            bool IsRelevantInput(Element owner, EngineObject obj)
                => obj.IsEnabled.Value && LayerVisible(owner, obj.GetCompositionTarget())
                    && (_includePortalInputs || obj.GetCompositionTarget() != CompositionTarget.Audio);
            int firstLayer = portal.ZIndex.Value + 1;
            int lastLayer = portal.ZIndex.Value + portal.Count.Value;
            Element[] candidates = (_portalFlow?.Candidates ?? (IReadOnlyList<Element>)scene.Children).Reverse().Where(candidate => candidate.IsEnabled.Value && candidate.ZIndex.Value >= firstLayer && candidate.ZIndex.Value <= lastLayer
                && IsPortalCandidate(candidate)
                && !_consumedPortalElements.Contains(candidate)
                && (_portalFlow?.Transitions.ContainsKey(candidate) == true || candidate.Objects.Any(obj => IsRelevantInput(candidate, obj))))
                .OrderBy(candidate => candidate.ZIndex.Value).ToArray();
            if (candidates.Length == 0) return [];
            // Offset writes still require a stable input set. Resize snapshots
            // describe one interval and can retain all plain providers in renderer
            // order (OrderedAdd puts later elements first when layers are equal).
            if (_includePortalInputs && (candidates.Length != 1 || HasCompetingFlowConsumer())) return null;
            var imported = new List<Node>();
            foreach (Element owner in candidates)
            {
                EngineObject[] objects = owner.Objects.Where(obj => IsRelevantInput(owner, obj)).ToArray();
                if (HasNonPlainProviderObject(objects)) return null;
                if (_portalFlow?.Transitions.TryGetValue(owner, out TransitionBoundary transition) == true)
                {
                    Element? partner = transition.Outgoing == owner ? transition.Incoming : transition.Outgoing;
                    if (partner != null && HasNonPlainProviderObject(partner.Objects.Where(obj => IsRelevantInput(partner, obj)).ToArray())) return null;
                    // The compositor replaces the participating clips with one
                    // drawable, including fades on clips with several objects.
                    objects = [new ClipTransitionPresenter { ZIndex = { Value = owner.ZIndex.Value } }];
                }
                if (IsClaimedByEarlierPortal(scene, owner, objects)) return null;
                _consumedPortalElements.Add(owner);
                imported.AddRange(objects.Select(obj => new Node(obj, [], PortalInput: true)));
            }
            return imported;
        }

        private bool HasCompetingFlowConsumer()
            => _element.Objects.Count(obj => IsEnabled(obj) && obj is DrawableTimeController) > 1
                || _element.Objects.Any(obj => IsEnabled(obj) && (obj is IPresenter<Drawable> && obj is not DrawableTimeController
                    || obj is IFlowOperator && obj is not DrawableTimeController));

        private static bool HasNonPlainProviderObject(EngineObject[] objects)
            => objects.Any(obj => obj is IFlowOperator or IPresenter<Drawable> || obj is not Drawable and not Sound);

        private bool IsClaimedByEarlierPortal(Scene scene, Element owner, EngineObject[] objects)
            => (_portalFlow?.Candidates ?? (IReadOnlyList<Element>)scene.Children).Any(other => other.IsEnabled.Value && other != _element && other.ZIndex.Value <= _element.ZIndex.Value && IsPortalCandidate(other)
                && objects.Any(obj => LayerVisible(other, obj.GetCompositionTarget()))
                && other.Objects.OfType<PortalObject>().Any(prior => prior.IsEnabled.Value
                    && (prior.Count.HasExpression || prior.ZIndex.Value < owner.ZIndex.Value && prior.ZIndex.Value + prior.Count.Value >= owner.ZIndex.Value)));

        private bool IsPortalCandidate(Element candidate) => _portalCandidates?.Contains(candidate)
            ?? (candidate.Start.Value < _element.Range.End && _element.Start.Value < candidate.Range.End);

        private bool LayerVisible(Element candidate, CompositionTarget target)
        {
            _layers.TryGetValue(candidate.ZIndex.Value, out TimelineLayer? layer);
            if (_hasSolo && (layer == null || !layer.IsSolo.Value)) return false;
            if (layer == null) return true;
            return target switch
            {
                CompositionTarget.Graphics => !layer.IsVideoMuted.Value,
                CompositionTarget.Audio => !layer.IsAudioMuted.Value,
                _ => !layer.IsVideoMuted.Value || !layer.IsAudioMuted.Value
            };
        }

        private void CollectNode(Node node, bool referenced = false)
        {
            if (node.PortalInput && !_includePortalInputs && !node.Opaque) return;
            if (node.Opaque)
            {
                // These unattached properties only carry a rejected constraint;
                // IsSupported prevents them from ever becoming an offset write.
                var target = new Target(Property.CreateExpression<MediaTime>(), null,
                    new MediaTimeMapping(_element, node.Object, Property.CreateExpression(100f), [], 60), _element.Length.Value);
                _targets.Add(target);
                if (node.PortalInput) _unresolvedPortalTargets++;
                return;
            }
            CollectFrom(node.Object, node.Inputs, node.FlowResolved, referenced, portalInput: node.PortalInput);
        }

        // Keep disabled streams in sync too. Detect cycles per path, rather than
        // discarding a second path whose time controller can impose tighter bounds.
        private void CollectFrom(EngineObject obj, IReadOnlyList<Node>? inputs = null, bool flowResolved = false, bool referenced = false, bool portalInput = false)
        {
            if (!_path.Add(obj)) return;
            if (inputs != null && obj is SoundGroup or DrawableGroup or DrawableDecorator)
            {
                foreach (Node child in inputs) CollectNode(child);
            }
            switch (obj)
            {
                case SourceVideo video:
                    {
                        MediaTime? duration = GetSourceDuration(video.Source.Value);
                        _targets.Add(new Target(video.OffsetPosition, duration,
                            new MediaTimeMapping(_element, video, video.Speed, _controllers, 60, duration, _timingPeers, _ignoreLoops, _timingRoles, portalInput), _element.Length.Value));
                    }
                    break;
                case SourceSound sound:
                    _targets.Add(new Target(sound.OffsetPosition, GetSourceDuration(sound.Source.Value),
                        new MediaTimeMapping(_element, sound, sound.Speed, _controllers, _sampleRate, timingPeers: _timingPeers, timingRoles: _timingRoles, portalInput: portalInput), _element.Length.Value));
                    break;
                case SceneSound sound:
                    _targets.Add(new Target(sound.OffsetPosition, sound.ReferencedScene.Value?.Duration.Value,
                        new MediaTimeMapping(_element, sound, sound.Speed, _controllers, _sampleRate, timingPeers: _timingPeers, timingRoles: _timingRoles, portalInput: portalInput), _element.Length.Value));
                    break;
                case SoundGroup group:
                    foreach (Sound child in group.Children) CollectFrom(child);
                    break;
                case DrawableGroup group:
                    foreach (Drawable child in group.Children) CollectFrom(child);
                    break;
                case DrawableDecorator decorator:
                    foreach (Drawable child in decorator.Children) CollectFrom(child);
                    break;
                case DrawableTimeController controller:
                    Node? input = inputs?.FirstOrDefault();
                    if (input?.Opaque == true)
                    {
                        CollectNode(input);
                        break;
                    }
                    if (input == null && flowResolved) break;
                    if (input == null && controller.Target.HasExpression)
                    {
                        // Represent an unknown target as an unsupported mapping, even
                        // when its stored target is null, so linked edits remain atomic.
                        // A consumed Flow input overrides Target and needs no such guard.
                        _targets.Add(new Target(controller.OffsetPosition, null,
                            new MediaTimeMapping(_element, controller, controller.Speed, _controllers, 60,
                                timingPeers: _timingPeers, timingRoles: _timingRoles), _element.Length.Value));
                        break;
                    }
                    if ((input?.Object ?? controller.Target.Value) is Drawable target)
                    {
                        bool applyMapping = IsEnabled(controller) || referenced || input == null;
                        if (applyMapping) _controllers.Add(new MediaTimeMapping.ControllerLink(controller, target));
                        if (input != null) CollectNode(input, referenced: true);
                        else CollectFrom(target, referenced: true);
                        if (applyMapping) _controllers.RemoveAt(_controllers.Count - 1);
                    }
                    break;
                case IPresenter<Drawable> presenter:
                    if (presenter.Target.HasExpression)
                    {
                        CollectNode(new Node(obj, [], Opaque: true));
                        break;
                    }
                    if (presenter.Target.Value is { } presented) CollectFrom(presented, referenced: true);
                    break;
            }
            _path.Remove(obj);
        }
    }

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
            IReadOnlyDictionary<Element, (TransitionBoundary? Start, TransitionBoundary? End)> boundaries, MediaTime time)
        {
            // OrderedAdd puts later elements first when their layers are equal.
            var ordered = candidates.Reverse().OrderBy(element => element.ZIndex.Value).ToList();
            var transitions = ElementTransitions.SelectActive(ordered, time, element => ranges[element],
                element => boundaries.GetValueOrDefault(element));
            return new(candidates, ordered.ToHashSet(), transitions);
        }
    }
}
