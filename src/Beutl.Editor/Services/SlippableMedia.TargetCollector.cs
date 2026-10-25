using Beutl.Audio;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
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
        private readonly List<(DrawableTimeController Controller, int Index)> _portalControllers = new();
        private readonly Dictionary<EngineObject, int> _objectOrder = new(ReferenceEqualityComparer.Instance);
        private readonly bool _hasPortal;
        private readonly IReadOnlySet<DrawableTimeController>? _reenabledControllers;
        private readonly Dictionary<MediaSource, MediaTime?> _sourceDurations;
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
            IReadOnlySet<DrawableTimeController>? reenabledControllers = null, Dictionary<MediaSource, MediaTime?>? sourceDurations = null,
            bool includePortalInputs = true)
        {
            _element = element;
            _timingPeers = timingPeers;
            _ignoreLoops = ignoreLoops;
            _timingRoles = timingRoles;
            _portalCandidates = portalCandidates;
            _includePortalInputs = includePortalInputs;
            _reenabledControllers = reenabledControllers;
            _sourceDurations = sourceDurations ?? new(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < element.Objects.Count; i++)
            {
                EngineObject obj = element.Objects[i];
                _objectOrder[obj] = i;
                if (obj is PortalObject { IsEnabled.Value: true }) _hasPortal = true;
                else if (_hasPortal && obj is DrawableTimeController controller) _portalControllers.Add((controller, i));
            }
            if (element.HierarchicalParent is Scene layerScene)
                foreach (TimelineLayer layer in layerScene.Layers) _layers.TryAdd(layer.ZIndex.Value, layer);
            _hasSolo = _layers.Values.Any(layer => layer.IsSolo.Value);
            Project? project = element.FindHierarchicalParent<Project>();
            _sampleRate = project.GetSampleRate();
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
                var collector = new TargetCollector(_element, _timingPeers, _ignoreLoops, _timingRoles, _portalCandidates,
                    enabled, _sourceDurations, _includePortalInputs);
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
                // An offline reader reports a zero duration; treat it as unknown so a
                // placeholder clip keeps the length the user asked for and stays resizable.
                VideoSource.Resource video => video.IsOffline || video.Duration <= MediaTime.Zero
                    ? null
                    : video.Duration,
                SoundSource.Resource sound => sound.Duration > MediaTime.Zero ? sound.Duration : null,
                _ => null
            };
            _sourceDurations.Add(source, duration);
            return duration;
        }

        private List<Node> BuildFlow()
        {
            var flow = new List<Node>();
            bool portalFlow = false;
            foreach (EngineObject obj in _element.Objects)
            {
                if (obj is PortalObject { IsEnabled.Value: true } portal)
                {
                    portalFlow = true;
                    List<Node>? imported;
                    if (_includePortalInputs)
                    {
                        if (portal.Clear.Value || portal.Clear.HasExpression) flow.Clear();
                        imported = portal.Clear.HasExpression ? null : ResolvePortal(portal);
                    }
                    else
                    {
                        // A dynamic Clear may or may not remove the earlier local inputs.
                        // Keep them so their bounds still apply; only a constant true clears.
                        if (!portal.Clear.HasExpression && portal.Clear.Value) flow.Clear();
                        imported = [];
                    }
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
                else if (obj.IsEnabled.Value && obj is SoundGroup or DrawableGroup or DrawableDecorator)
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
            if (portal.Count.Value <= 0) return [];
            if (_opaqueConsumption || _element.HierarchicalParent is not Scene scene) return null;
            int firstLayer = portal.ZIndex.Value + 1;
            int lastLayer = portal.ZIndex.Value + portal.Count.Value;
            Element[] candidates = scene.Children.Where(candidate => candidate.IsEnabled.Value && candidate.ZIndex.Value >= firstLayer && candidate.ZIndex.Value <= lastLayer
                && IsPortalCandidate(candidate)
                && !_consumedPortalElements.Contains(candidate)
                && candidate.Objects.Any(obj => obj.IsEnabled.Value && LayerVisible(candidate, obj.GetCompositionTarget()))).ToArray();
            if (candidates.Length == 0) return [];
            // A single plain provider has one stable input order whenever it is
            // active. Switching, nested, or competing portals need a richer flow
            // model; never substitute the controller's stored target for them.
            if (candidates.Length != 1) return null;
            Element owner = candidates[0];
            if (HasCompetingFlowConsumer()) return null;
            EngineObject[] objects = owner.Objects.Where(obj => obj.IsEnabled.Value && LayerVisible(owner, obj.GetCompositionTarget())).ToArray();
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
            => scene.Children.Any(other => other.IsEnabled.Value && other != _element && other.ZIndex.Value <= _element.ZIndex.Value && IsPortalCandidate(other)
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
            if (node.Opaque)
            {
                // These unattached properties only carry a rejected constraint;
                // IsSupported prevents them from ever becoming an offset write.
                _targets.Add(new Target(Property.CreateExpression<MediaTime>(), null,
                    new MediaTimeMapping(_element, node.Object, Property.CreateExpression(100f), [], 60), _element.Length.Value));
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
                        var videoTarget = new Target(video.OffsetPosition, duration,
                            new MediaTimeMapping(_element, video, video.Speed, _controllers, 60, duration, _timingPeers, _ignoreLoops, _timingRoles, portalInput), _element.Length.Value);
                        _targets.Add(videoTarget);
                        if (!_includePortalInputs && _hasPortal)
                        {
                            int mediaIndex = GetTopLevelIndex(video);
                            AddPortalRouteMappings(videoTarget, video, mediaIndex,
                                CanPrecedeWithDrawable(mediaIndex),
                                links => new MediaTimeMapping(_element, video, video.Speed, links, 60,
                                    duration, _timingPeers, _ignoreLoops, _timingRoles));
                        }
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

        // A portal import can start or end while the element is resized, changing
        // which later controller consumes local media. Consider each suffix of the
        // controllers that follow the media; the tightest bound wins.
        private void AddPortalRouteMappings(Target target, Drawable media, int mediaIndex, bool canBeDirect,
            Func<IReadOnlyList<MediaTimeMapping.ControllerLink>, MediaTimeMapping> create)
        {
            if (canBeDirect) target.SampleMappings.Add(create([]));

            var controllers = new List<DrawableTimeController>();
            foreach ((DrawableTimeController controller, int index) in _portalControllers)
                if (index > mediaIndex) controllers.Add(controller);
            if (controllers.Count > 10) return;
            for (int mask = 1; mask < 1 << controllers.Count; mask++)
            {
                var links = new List<MediaTimeMapping.ControllerLink>();
                Drawable input = media;
                for (int i = 0; i < controllers.Count; i++)
                {
                    if ((mask & (1 << i)) == 0) continue;
                    links.Add(new MediaTimeMapping.ControllerLink(controllers[i], input));
                    input = controllers[i];
                }

                target.SampleMappings.Add(create(links));
            }
        }

        private int GetTopLevelIndex(EngineObject obj)
        {
            for (EngineObject? current = obj; current != null; current = current.HierarchicalParent as EngineObject)
                if (_objectOrder.TryGetValue(current, out int index)) return index;
            return -1;
        }

        // A later controller can leave this media alone only when an earlier drawable
        // or possible portal import can take its place in the Flow.
        private bool CanPrecedeWithDrawable(int mediaIndex)
        {
            if (mediaIndex < 0) return true;
            for (int i = 0; i < mediaIndex; i++)
            {
                EngineObject obj = _element.Objects[i];
                if (obj is PortalObject { IsEnabled.Value: true } portal
                    && (portal.Count.HasExpression || portal.Count.Value > 0))
                {
                    return true;
                }

                if (obj is Drawable drawable && drawable.IsEnabled.Value) return true;
            }

            return false;
        }
    }
}
