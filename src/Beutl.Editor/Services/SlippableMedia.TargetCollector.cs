using Beutl.Audio;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
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
        private readonly IReadOnlySet<DrawableTimeController>? _reenabledControllers;
        private readonly List<Target> _targets = new();
        private readonly List<DrawableTimeController> _disabledFlowControllers = new();
        private readonly HashSet<object> _path = new();
        private readonly List<MediaTimeMapping.ControllerLink> _controllers = new();
        private readonly Dictionary<int, TimelineLayer> _layers = new();
        private readonly bool _hasSolo;
        private readonly int _sampleRate;
        private readonly HashSet<Element> _consumedPortalElements = new();
        private bool _opaqueConsumption;

        public TargetCollector(Element element, IReadOnlySet<Element>? timingPeers, bool ignoreLoops,
            IReadOnlyDictionary<Element, MediaTimeMapping.TrimRole>? timingRoles, IReadOnlySet<Element>? portalCandidates,
            IReadOnlySet<DrawableTimeController>? reenabledControllers = null)
        {
            _element = element;
            _timingPeers = timingPeers;
            _ignoreLoops = ignoreLoops;
            _timingRoles = timingRoles;
            _portalCandidates = portalCandidates;
            _reenabledControllers = reenabledControllers;
            if (element.HierarchicalParent is Scene layerScene)
                foreach (TimelineLayer layer in layerScene.Layers) _layers.TryAdd(layer.ZIndex, layer);
            _hasSolo = _layers.Values.Any(layer => layer.IsSolo);
            Project? project = element.FindHierarchicalParent<Project>();
            _sampleRate = project?.Variables.TryGetValue(ProjectVariableKeys.SampleRate, out string? value) == true
                && int.TryParse(value, out int rate) && rate > 0 ? rate : 44100;
        }

        public List<Target> Collect()
        {
            foreach (Node node in BuildFlow()) CollectNode(node);
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
                var collector = new TargetCollector(_element, _timingPeers, _ignoreLoops, _timingRoles, _portalCandidates, enabled);
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
            => obj.IsEnabled || obj is DrawableTimeController controller && _reenabledControllers?.Contains(controller) == true;

        private List<Node> BuildFlow()
        {
            var flow = new List<Node>();
            bool portalFlow = false;
            foreach (EngineObject obj in _element.Objects)
            {
                if (obj is PortalObject { IsEnabled: true } portal)
                {
                    portalFlow = true;
                    if (portal.Clear.CurrentValue || portal.Clear.HasExpression) flow.Clear();
                    List<Node>? imported = portal.Clear.HasExpression ? null : ResolvePortal(portal);
                    if (imported == null)
                    {
                        flow.Add(new Node(portal, [], Opaque: true));
                        _opaqueConsumption = true;
                    }
                    else flow.AddRange(imported);
                    flow.Add(new Node(portal, []));
                    continue;
                }
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
                        else
                        {
                            _disabledFlowControllers.Add(controller);
                        }
                    }
                }
                else if (obj.IsEnabled && obj is SoundGroup or DrawableGroup or DrawableDecorator)
                {
                    bool Consumes(Node node) => node.Opaque || IsEnabled(node.Object)
                        && (obj is SoundGroup ? node.Object is Sound : node.Object is Drawable);
                    inputs = flow.FindAll(Consumes);
                    flow.RemoveAll(Consumes);
                }
                flow.Add(new Node(obj, inputs, FlowResolved: portalFlow));
            }
            return flow;
        }

        private List<Node>? ResolvePortal(PortalObject portal)
        {
            if (portal.Count.HasExpression) return null;
            if (portal.Count.CurrentValue <= 0) return [];
            if (_opaqueConsumption || _element.HierarchicalParent is not Scene scene) return null;
            int firstLayer = portal.ZIndex + 1;
            int lastLayer = portal.ZIndex + portal.Count.CurrentValue;
            Element[] candidates = scene.Children.Where(candidate => candidate.IsEnabled && candidate.ZIndex >= firstLayer && candidate.ZIndex <= lastLayer
                && IsPortalCandidate(candidate)
                && !_consumedPortalElements.Contains(candidate)
                && candidate.Objects.Any(obj => obj.IsEnabled && LayerVisible(candidate, obj.GetCompositionTarget()))).ToArray();
            if (candidates.Length == 0) return [];
            // A single plain provider has one stable input order whenever it is
            // active. Switching, nested, or competing portals need a richer flow
            // model; never substitute the controller's stored target for them.
            if (candidates.Length != 1) return null;
            Element owner = candidates[0];
            if (HasCompetingFlowConsumer()) return null;
            EngineObject[] objects = owner.Objects.Where(obj => obj.IsEnabled && LayerVisible(owner, obj.GetCompositionTarget())).ToArray();
            if (HasNonPlainProviderObject(objects)) return null;
            if (IsClaimedByEarlierPortal(scene, owner, objects)) return null;
            _consumedPortalElements.Add(owner);
            return objects.Select(obj => new Node(obj, [], PortalInput: true)).ToList();
        }

        private bool HasCompetingFlowConsumer()
            => _element.Objects.Count(obj => IsEnabled(obj) && obj is DrawableTimeController) > 1
                || _element.Objects.Any(obj => IsEnabled(obj) && (obj is IPresenter<Drawable> && obj is not DrawableTimeController
                    || obj is IFlowOperator && obj is not DrawableTimeController));

        private static bool HasNonPlainProviderObject(EngineObject[] objects)
            => objects.Any(obj => obj is IFlowOperator or IPresenter<Drawable> || obj is not Drawable and not Sound);

        private bool IsClaimedByEarlierPortal(Scene scene, Element owner, EngineObject[] objects)
            => scene.Children.Any(other => other.IsEnabled && other != _element && other.ZIndex <= _element.ZIndex && IsPortalCandidate(other)
                && objects.Any(obj => LayerVisible(other, obj.GetCompositionTarget()))
                && other.Objects.OfType<PortalObject>().Any(prior => prior.IsEnabled
                    && (prior.Count.HasExpression || prior.ZIndex < owner.ZIndex && prior.ZIndex + prior.Count.CurrentValue >= owner.ZIndex)));

        private bool IsPortalCandidate(Element candidate) => _portalCandidates?.Contains(candidate)
            ?? (candidate.Start < _element.Range.End && _element.Start < candidate.Range.End);

        private bool LayerVisible(Element candidate, CompositionTarget target)
        {
            _layers.TryGetValue(candidate.ZIndex, out TimelineLayer? layer);
            if (_hasSolo && (layer == null || !layer.IsSolo)) return false;
            if (layer == null) return true;
            return target switch
            {
                CompositionTarget.Graphics => !layer.IsVideoMuted,
                CompositionTarget.Audio => !layer.IsAudioMuted,
                _ => !layer.IsVideoMuted || !layer.IsAudioMuted
            };
        }

        private void CollectNode(Node node, bool referenced = false)
        {
            if (node.Opaque)
            {
                // These unattached properties only carry a rejected constraint;
                // IsSupported prevents them from ever becoming an offset write.
                _targets.Add(new Target(Property.Create<TimeSpan>(), null,
                    new MediaTimeMapping(_element, node.Object, Property.Create(100f), [], 60), _element.Length));
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
                    using (var resource = video.Source.CurrentValue?.ToResource(CompositionContext.Default))
                    {
                        _targets.Add(new Target(video.OffsetPosition, resource?.Duration,
                            new MediaTimeMapping(_element, video, video.Speed, _controllers, 60, resource?.Duration, _timingPeers, _ignoreLoops, _timingRoles, portalInput), _element.Length));
                    }
                    break;
                case SourceSound sound:
                    using (var resource = sound.Source.CurrentValue?.ToResource(CompositionContext.Default))
                    {
                        _targets.Add(new Target(sound.OffsetPosition, resource?.Duration > TimeSpan.Zero ? resource.Duration : null,
                            new MediaTimeMapping(_element, sound, sound.Speed, _controllers, _sampleRate, timingPeers: _timingPeers, timingRoles: _timingRoles, portalInput: portalInput), _element.Length));
                    }
                    break;
                case SceneSound sound:
                    _targets.Add(new Target(sound.OffsetPosition, sound.ReferencedScene.CurrentValue?.Duration,
                        new MediaTimeMapping(_element, sound, sound.Speed, _controllers, _sampleRate, timingPeers: _timingPeers, timingRoles: _timingRoles, portalInput: portalInput), _element.Length));
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
                                timingPeers: _timingPeers, timingRoles: _timingRoles), _element.Length));
                        break;
                    }
                    if ((input?.Object ?? controller.Target.CurrentValue) is Drawable target)
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
                    if (presenter.Target.CurrentValue is { } presented) CollectFrom(presented, referenced: true);
                    break;
            }
            _path.Remove(obj);
        }
    }
}
