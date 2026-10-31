using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Beutl.Collections.Pooled;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Transitions;
using Beutl.Media;
using Beutl.Media.Proxy;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed partial class SceneCompositor : ICompositor
{
    private readonly ConditionalWeakTable<EngineObject, ResourceEntry> _resourceCache = new();
    private readonly ConcurrentQueue<EngineObject.Resource> _detachedResources = new();
    private readonly object _resourceCacheLock = new();
    // Sample sessions retain only resources used by their current evaluation. Ordinary frame caches
    // keep their existing lifetime; the session owns this set and releases unused entries afterward.
    private HashSet<EngineObject.Resource>? _usedSampleResources;
    // One presenter per transition draws it for this compositor; it lives as long as the transition does.
    private readonly ConditionalWeakTable<ClipTransition, ClipTransitionPresenter> _transitionPresenters = new();

    // Mute flags are read live from the layers inside the snapshot, so only the
    // lookup shape (membership, ZIndex) and HasSolo require invalidation.
    private volatile SceneLayerSnapshot? _layerSnapshot;
    // The elements that can take part in a transition, kept until the scene is edited. The version moves
    // on every edit, so a set built while an edit lands is not kept past it.
    private volatile ParticipantSnapshot? _participants;
    private int _participantsVersion;
    private volatile AudioEligibilitySnapshot? _audioEligibility;
    private int _audioEligibilityVersion;

    public SceneCompositor(Scene scene)
    {
        Scene = scene;
        _layerQueries = new(this, CreateLayerQuery);
        _audioQueries = new(this, CreateAudioQuery);
        Scene.Layers.CollectionChanged += OnLayersCollectionChanged;
        Scene.Edited += OnSceneEdited;
        Scene.Children.CollectionChanged += OnChildrenCollectionChanged;
        Scene.Layers.Attached += OnLayerAttached;
        Scene.Layers.Detached += OnLayerDetached;
        foreach (TimelineLayer layer in Scene.Layers)
        {
            ObserveLayer(layer);
        }
    }

    public Scene Scene { get; }

    public bool DisableResourceShare { get; init; }

    public bool ForceOriginalSource { get; init; }

    // Replaces a compositor whose scene or sharing/source settings no longer match the context and
    // creates one for a referenced scene. The field is cleared before construction, so a constructor
    // that throws leaves no disposed compositor behind.
    internal static void Refresh(ref SceneCompositor? field, Scene? scene, CompositionContext context)
    {
        bool forceOriginalSource = !context.PreferProxy;
        if (field?.Scene != scene
            || field?.DisableResourceShare != context.DisableResourceShare
            || field?.ForceOriginalSource != forceOriginalSource)
        {
            field?.Dispose();
            field = null;
        }

        if (scene != null && field == null)
        {
            field = new SceneCompositor(scene)
            {
                DisableResourceShare = context.DisableResourceShare,
                ForceOriginalSource = forceOriginalSource,
            };
        }
    }

    private sealed class CompositorContext : CompositionContext, ISceneCompositionContext
    {
        private readonly SceneCompositor _compositor;

        public CompositorContext(MediaTime time,
            SceneCompositor compositor,
            IList<EngineObject.Resource> flow,
            IList<Element> currentElements,
            CompositionTarget target, EvaluationServices? services = null) : base(time)
        {
            _compositor = compositor;
            CurrentElements = currentElements;
            Target = target;
            GraphicsState = target == CompositionTarget.Graphics ? new(time) : null;
            Flow = flow;
            DisableResourceShare = compositor.DisableResourceShare;
            Conditions = new CompositionConditions
            {
                PreferProxy = !compositor.ForceOriginalSource
                    && GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode.Value == PreviewSourceMode.PreferProxy,
                PreferredProxyPreset = ToPreset(GlobalConfiguration.Instance.ProxyStoreConfig.DefaultPreset.Value),
                TargetDomain = new Rect(default, compositor.Scene.FrameSize.Value.ToSize(1)),
                FrameRate = compositor.Scene.FindHierarchicalParent<Project>().GetFrameRate(),
                EvaluationServices = services?.Fork(compositor, this) ?? new EvaluationServices(compositor, this),
            };
        }

        public override CompositionEvaluationSession CreateEvaluationSession()
            => new SceneEvaluationSession(_compositor.Scene, Target, _compositor.ForceOriginalSource,
                (EvaluationServices)EvaluationServices!);

        public void InheritConditions(CompositionContext? conditions)
        {
            if (conditions is null) return;
            // A nested scene keeps its own frame domain and query services.
            Conditions = conditions.Conditions with { TargetDomain = TargetDomain, EvaluationServices = EvaluationServices };
            DisableResourceShare |= conditions.DisableResourceShare;
            InputBoundaries = conditions.InputBoundaries;
        }

        public IList<Element> CurrentElements { get; set; }

        // A captured context outlives this frame's pooled element list, which is released after evaluation.
        public override CompositionContext WithTime(MediaTime time)
        {
            var result = (CompositorContext)base.WithTime(time);
            result.CurrentElements = [];
            return result;
        }

        public CompositionTarget Target { get; set; }

        // The frame's own time while a transition evaluates one of its sides at a held time. Elements a
        // portal draws into that side are not the side's own content and have not ended, so they play on
        // at the frame's time, and both sides share one evaluation of them.
        public MediaTime? FrameTime { get; set; }

        public GraphicsFrameState? GraphicsState { get; set; }

        public bool IsMatteSource { get; init; }

        public TrackMatteContent ResolveMatte(Element element)
        {
            TrackMatteContent source = element.GetMatteSourceZIndex(_compositor.Scene, GraphicsState!.Time) is { } z
                ? _compositor.GetMatteSource(z, this) : _compositor._emptyMatteSource;
            ContainsScene3D |= GraphicsState.ContainsScene3D;
            return source;
        }

        private static ProxyPreset ToPreset(int value)
        {
            return Enum.IsDefined(typeof(ProxyPreset), value)
                ? (ProxyPreset)value
                : ProxyPreset.Quarter;
        }

        public void EvaluateElementIntoFlow(Element element)
        {
            using var tmpObjects = new PooledList<EngineObject>();
            MediaTime sideTime = Time;
            Time = FrameTime ?? sideTime;
            try
            {
                _compositor.CollectResourcesFromElement(element, this, tmpObjects);
            }
            finally
            {
                Time = sideTime;
            }
        }
    }

    public CompositionFrame EvaluateGraphics(MediaTime time) => EvaluateGraphics(time, null, null);

    /// <summary>Evaluates a frame with the output's actual rate and cancellation policy.</summary>
    public CompositionFrame EvaluateGraphics(MediaTime time, Rational? frameRate, CancellationToken cancellationToken = default)
        => EvaluateGraphics(time, null, null, null, cancellationToken, frameRate);

    /// <summary>Evaluates a nested scene while inheriting its caller's evaluation conditions.</summary>
    internal CompositionFrame EvaluateGraphicsWithContext(MediaTime time, CompositionContext conditions)
        => EvaluateGraphics(time, null, null, conditions);

    // Inherited conditions bring their own cancellation and frame rate; cancellationToken and frameRate apply
    // only without them.
    private CompositionFrame EvaluateGraphics(MediaTime time, int? layer, EvaluationServices? services,
        CompositionContext? conditions = null, CancellationToken cancellationToken = default, Rational? frameRate = null)
    {
        CancellationToken token = conditions?.CancellationToken ?? cancellationToken;
        token.ThrowIfCancellationRequested();
        // A layer query evaluates the requester's frame, not one of this compositor's own.
        if (services is null) BeginQueryGeneration();
        DisposeDetachedResources();
        using var currentElements = new PooledList<Element>();
        SortLayers(time, currentElements, CompositionTarget.Graphics);

        using var tmpObjects = new PooledList<EngineObject>();
        using var flow = new PooledList<EngineObject.Resource>();
        using var allResources = new PooledList<EngineObject.Resource>();
        var ctx = new CompositorContext(time, this, flow, currentElements, CompositionTarget.Graphics);
        ctx.InheritConditions(conditions);
        if (conditions is null)
        {
            ctx.CancellationToken = cancellationToken;
            if (frameRate.HasValue) ctx.FrameRate = frameRate.Value;
        }
        if (services is not null)
            ctx.EvaluationServices = services;

        // 途中でcurrentElementsが変わる可能性があるのでforループで回す
        for (int index = 0; index < currentElements.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            if (layer.HasValue && currentElements[index].ZIndex.Value != layer.Value) continue;
            flow.Clear();
            CollectResourcesFromElement(currentElements[index], ctx, tmpObjects);

            allResources.AddRange(flow.Span);
        }

        var frame = new CompositionFrame(
            [.. allResources],
            new(time, new MediaTime(1)),
            Scene.FrameSize.Value,
            null)
        {
            ContainsScene3D = ctx.ContainsScene3D || ctx.GraphicsState?.ContainsScene3D == true,
            CancellationToken = token,
        };
        if (services is null)
            ((EvaluationServices)ctx.EvaluationServices!).Frame = frame;
        return frame;
    }

    public CompositionFrame EvaluateAudio(TimeRange timeRange) => EvaluateAudio(timeRange, null);

    internal CompositionFrame EvaluateAudio(TimeRange timeRange, CompositionContext? conditions)
    {
        conditions?.CancellationToken.ThrowIfCancellationRequested();
        BeginQueryGeneration();
        DisposeDetachedResources();
        AudioEligibilitySnapshot eligibility = GetAudioEligibility();

        using var currentElements = new PooledList<Element>();
        SortLayers(timeRange, currentElements, CompositionTarget.Audio);

        using var activeObjects = new PooledList<EngineObject>();
        foreach (Element element in currentElements)
        {
            if (eligibility.ByElement.TryGetValue(element, out EngineObject[]? objects))
                activeObjects.AddRange(objects);
        }

        using var eligibleObjects = new PooledList<EngineObject>();

        using var tmpObjects = new PooledList<EngineObject>();
        using var flow = new PooledList<EngineObject.Resource>();
        using var allResources = new PooledList<EngineObject.Resource>();
        var ctx = new CompositorContext(timeRange.Start, this, flow, currentElements, CompositionTarget.Audio,
            conditions?.EvaluationServices as EvaluationServices);
        ctx.InheritConditions(conditions);

        // 途中でcurrentElementsが変わる可能性があるのでforループで回す
        for (int index = 0; index < currentElements.Count; index++)
        {
            flow.Clear();
            CollectResourcesFromElement(currentElements[index], ctx, tmpObjects);

            allResources.AddRange(flow.Span);
            foreach (EngineObject.Resource resource in flow.Span)
            {
                eligibleObjects.Add(resource.RequireOriginal());
            }
        }

        return new CompositionFrame(
            [.. allResources],
            timeRange,
            Scene.FrameSize.Value,
            eligibility.Objects.WithChanges(activeObjects, eligibleObjects));
    }

    // Analyze a selected element through the same flow as playback, including its SoundGroups and
    // portal inputs. Other tracks are available to portals but are not mixed as independent roots.
    internal CompositionFrame EvaluateElementAudio(Element element, TimeRange timeRange)
    {
        DisposeDetachedResources();
        using var currentElements = new PooledList<Element>();
        SortLayers(timeRange, currentElements, CompositionTarget.Audio);
        using var objects = new PooledList<EngineObject>();
        using var flow = new PooledList<EngineObject.Resource>();
        var context = new CompositorContext(timeRange.Start, this, flow, currentElements, CompositionTarget.Audio);
        CollectResourcesFromElement(element, context, objects);
        return new CompositionFrame([.. flow], timeRange, Scene.FrameSize.Value,
            new CompositionEligibility(flow.Select(resource => resource.RequireOriginal())));
    }

    private AudioEligibilitySnapshot GetAudioEligibility()
    {
        int version = Volatile.Read(ref _audioEligibilityVersion);
        if (_audioEligibility is { } cached && cached.Version == version) return cached;

        SceneLayerSnapshot layers = GetLayerSnapshot();
        var byElement = new Dictionary<Element, EngineObject[]>(ReferenceEqualityComparer.Instance);
        using var objects = new PooledList<EngineObject>();
        using var allObjects = new PooledList<EngineObject>();
        foreach (Element element in Scene.Children)
        {
            if (!element.IsEnabled.Value || layers.ShouldSkip(element.ZIndex.Value, CompositionTarget.Audio)) continue;
            objects.Clear();
            element.CollectObjects(CompositionTarget.Audio, objects);
            if (objects.Count == 0) continue;
            byElement.Add(element, objects.ToArray());
            allObjects.AddRange(objects.Span);
        }
        return _audioEligibility = new AudioEligibilitySnapshot(version, new CompositionEligibility(allObjects), byElement);
    }

    private sealed record AudioEligibilitySnapshot(int Version, CompositionEligibility Objects,
        Dictionary<Element, EngineObject[]> ByElement);

    private void CollectResourcesFromElement(
        Element element, CompositorContext context, PooledList<EngineObject> tmpObjects)
    {
        if (context.Target == CompositionTarget.Graphics
            && GetTransitionParticipants().Contains(element)
            && ElementTransitions.TryGetActive(element, context.Time, out TransitionBoundary boundary))
        {
            if (context.IsMatteSource
                || boundary.Outgoing?.IsVisibleInOutput.Value == true
                || boundary.Incoming?.IsVisibleInOutput.Value == true)
            {
                CollectTransition(boundary, context);
            }
            return;
        }

        CollectElementObjects(element, context, tmpObjects);
    }

    // Evaluates both elements of an active boundary transition, each at its own time, and hands them to
    // the transition's presenter, which takes the place of both elements' drawables in the flow.
    private void CollectTransition(in TransitionBoundary boundary, CompositorContext context)
    {
        using var from = new PooledList<EngineObject.Resource>();
        using var to = new PooledList<EngineObject.Resource>();
        using var tmpObjects = new PooledList<EngineObject>();
        IList<EngineObject.Resource>? oldFlow = context.Flow;
        MediaTime time = context.Time;
        IList<Element> elements = context.CurrentElements;
        // A portal takes the elements it draws out of the current ones, so each side starts from the same
        // elements; whatever either side took stays out of the frame afterwards.
        Element[] inputs = [.. elements];
        var incomingInputs = new List<Element>(inputs);
        MediaTime? frameTime = context.FrameTime;
        context.FrameTime = time;
        try
        {
            if (boundary.Outgoing is { } outgoing)
            {
                context.Flow = from;
                context.Time = boundary.GetOutgoingTime(time);
                CollectElementObjects(outgoing, context, tmpObjects);
            }

            if (boundary.Incoming is { } incoming)
            {
                context.Flow = to;
                context.Time = boundary.GetIncomingTime(time);
                context.CurrentElements = incomingInputs;
                CollectElementObjects(incoming, context, tmpObjects);
            }
        }
        finally
        {
            context.FrameTime = frameTime;
            context.CurrentElements = elements;
            foreach (Element input in inputs)
            {
                if (!incomingInputs.Contains(input))
                {
                    elements.Remove(input);
                }
            }

            context.Flow = oldFlow;
            context.Time = time;
        }

        var transitionResource = (ClipTransition.Resource)GetOrCreateResource(boundary.Transition, context);
        ClipTransitionPresenter presenter = _transitionPresenters.GetValue(
            boundary.Transition,
            static _ => new ClipTransitionPresenter());
        int zIndex = (boundary.Incoming ?? boundary.Outgoing)!.ZIndex.Value;
        if (presenter.ZIndex.Value != zIndex)
        {
            presenter.ZIndex.Value = zIndex;
        }

        var presenterResource = (ClipTransitionPresenter.Resource)GetOrCreateResource(presenter, context);
        float progress = ClipTransition.Ease(transitionResource.Easing, boundary.GetLinearProgress(time));
        presenterResource.SetInputs(transitionResource, from.Span, to.Span, progress);
        oldFlow?.Add(presenterResource);
    }

    private void CollectElementObjects(
        Element element, CompositorContext context, PooledList<EngineObject> tmpObjects)
    {
        // Check at the element, including transition sides and portal inputs. A matte evaluates the
        // same layer independently of its output visibility.
        if (context.Target == CompositionTarget.Graphics && !context.IsMatteSource
            && !element.IsVisibleInOutput.Value) return;
        // Cyclic matte references have no source contribution at the repeated element.
        if (context.GraphicsState is { } state && !state.Evaluating.Add(element)) return;
        using var flow = new PooledList<EngineObject.Resource>();
        var oldFlow = context.Flow;
        context.Flow = flow;
        try
        {
            tmpObjects.Clear();
            element.CollectObjects(context.Target, tmpObjects);
            foreach (EngineObject obj in tmpObjects.Span)
            {
                flow.Add(GetOrCreateResource(obj, context));
            }

            if (context.GraphicsState is { } frame && element.MatteMode.Value != MatteMode.None
                && (element.MatteSourceKind.Value != MatteSourceKind.Layer || element.MatteSourceZIndex.Value != null))
            {
                ApplyTrackMatte(element, element.GetMatteSourceZIndex(Scene, frame.Time), context, flow);
            }

            foreach (EngineObject.Resource resource in flow)
            {
                oldFlow?.Add(resource);
            }
        }
        finally
        {
            context.Flow = oldFlow;
            context.GraphicsState?.Evaluating.Remove(element);
        }
    }

    private sealed class ResourceEntry(EngineObject.Resource resource, EventHandler<HierarchyAttachmentEventArgs> detached)
    {
        public EngineObject.Resource Resource { get; set; } = resource;
        public EventHandler<HierarchyAttachmentEventArgs> Detached { get; } = detached;
    }

    private EngineObject.Resource GetOrCreateResource(EngineObject obj, CompositionContext context)
    {
        EngineObject? previousOwner = context.FlowOwner;
        context.FlowOwner = obj;
        try
        {
            EngineObject.Resource resource = GetOrCreateResourceCore(obj, context);
            _usedSampleResources?.Add(resource);
            return resource;
        }
        finally
        {
            context.FlowOwner = previousOwner;
        }
    }

    private EngineObject.Resource GetOrCreateResourceCore(EngineObject obj, CompositionContext context)
    {
        bool matte = context is CompositorContext { IsMatteSource: true };
        var cache = matte ? _matteResourceCache : _resourceCache;
        if (!cache.TryGetValue(obj, out var entry))
        {
            var resource = obj.ToResource(context);
            entry = new ResourceEntry(resource, AddDetachedHandler(obj, matte));
            cache.Add(obj, entry);
        }
        else if (entry.Resource.IsDisposed)
        {
            entry.Resource = obj.ToResource(context);
        }
        else
        {
            bool _ = false;
            entry.Resource.Reconcile(obj, context, ref _);
        }

        return entry.Resource;
    }

    private EventHandler<HierarchyAttachmentEventArgs> AddDetachedHandler(EngineObject obj, bool matte)
    {
        var weakRef = new WeakReference<SceneCompositor>(this);

        void Handler(object? sender, HierarchyAttachmentEventArgs e)
        {
            if (sender is not EngineObject senderObj) return;

            if (weakRef.TryGetTarget(out SceneCompositor? compositor))
            {
                lock (compositor._resourceCacheLock)
                {
                    var cache = matte ? compositor._matteResourceCache : compositor._resourceCache;
                    if (cache.TryGetValue(senderObj, out var entry))
                    {
                        cache.Remove(senderObj);
                        // Detachment runs on the editing thread while the current frame may still use this
                        // resource's native handles. Transfer ownership before teardown can drain the queue.
                        compositor._detachedResources.Enqueue(entry.Resource);
                    }
                }
            }

            senderObj.DetachedFromHierarchy -= Handler;
        }

        obj.DetachedFromHierarchy += Handler;
        return Handler;
    }

    private void DisposeDetachedResources()
    {
        while (_detachedResources.TryDequeue(out var resource))
        {
            resource.Dispose();
        }
    }

    private void ReleaseCachedResources(HashSet<EngineObject.Resource>? retained = null)
    {
        lock (_resourceCacheLock)
        {
            foreach (var cache in new[] { _resourceCache, _matteResourceCache })
            {
                foreach (var pair in cache)
                {
                    if (retained?.Contains(pair.Value.Resource) == true) continue;
                    pair.Key.DetachedFromHierarchy -= pair.Value.Detached;
                    cache.Remove(pair.Key);
                    _detachedResources.Enqueue(pair.Value.Resource);
                }
            }
        }

        // Release outside the lock because disposal can itself trigger hierarchy callbacks.
        DisposeDetachedResources();
    }

    // timeに掛かるElementを、solo/muteでフィルタしつつZIndex順に振り分ける
    private void SortLayers(MediaTime time, PooledList<Element> currentElements, CompositionTarget target, bool ignoreSolo = false)
    {
        SceneLayerSnapshot snapshot = GetLayerSnapshot();
        bool graphics = target == CompositionTarget.Graphics;
        SceneElementIndex index = Scene.ElementIndex;
        using var candidates = new PooledList<Element>();
        MediaTime tolerance = graphics ? ElementTransitions.AdjacencyTolerance : MediaTime.Zero;
        index.Query(time - tolerance, time + tolerance, candidates);
        foreach (Element item in candidates)
        {
            if (!item.IsEnabled.Value || !(graphics ? item.CoversGraphicsTime(time) : item.Range.Contains(time))) continue;
            if (snapshot.ShouldSkip(item.ZIndex.Value, target, ignoreSolo)) continue;
            currentElements.Add(item);
        }
        index.Sort(currentElements);

        if (graphics)
        {
            KeepOneElementPerTransition(time, currentElements);
        }
    }

    // Both elements of a boundary are drawn by its transition, so when both fall on this frame (they
    // overlap, or touch within the tolerance) only the first stays in the list to collect it. Past the
    // transition the incoming element has taken over, so an outgoing element that runs on beyond it (the
    // two overlap and the span stops at the incoming element's middle) stays hidden.
    private void KeepOneElementPerTransition(MediaTime time, PooledList<Element> currentElements)
    {
        HashSet<Element> participants = GetTransitionParticipants();
        if (participants.Count == 0) return;

        for (int i = currentElements.Count - 1; i >= 0; i--)
        {
            Element element = currentElements[i];
            if (participants.Contains(element)
                && ElementTransitions.GetBoundaryAtEnd(element) is { Incoming: { } next } end
                && time >= end.Region.End
                && currentElements.Contains(next))
            {
                currentElements.RemoveAt(i);
            }
        }

        for (int i = 0; i < currentElements.Count; i++)
        {
            if (participants.Contains(currentElements[i])
                && ElementTransitions.TryGetActive(currentElements[i], time, out TransitionBoundary boundary)
                && boundary is { Outgoing: { } outgoing, Incoming: { } incoming })
            {
                Element partner = ReferenceEquals(currentElements[i], outgoing) ? incoming : outgoing;
                int index = currentElements.IndexOf(partner);
                if (index > i)
                {
                    currentElements.RemoveAt(index);
                }
            }
        }
    }

    // timeRangeに掛かるElementを、solo/muteでフィルタしつつZIndex順に振り分ける
    private void SortLayers(TimeRange timeRange, PooledList<Element> currentElements, CompositionTarget target)
    {
        SceneLayerSnapshot snapshot = GetLayerSnapshot();
        SceneElementIndex index = Scene.ElementIndex;
        using var candidates = new PooledList<Element>();
        index.Query(timeRange.Start, timeRange.End, candidates);
        foreach (Element item in candidates)
        {
            if (!item.IsEnabled.Value || !item.Range.Intersects(timeRange)) continue;
            if (snapshot.ShouldSkip(item.ZIndex.Value, target)) continue;
            currentElements.Add(item);
        }
        index.Sort(currentElements);
    }

    // Concurrent rebuilds after an invalidation are benign: each produces an
    // equivalent snapshot and the last write wins.
    private SceneLayerSnapshot GetLayerSnapshot() => _layerSnapshot ??= SceneLayerSnapshot.Build(Scene);

    private void OnLayersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _layerSnapshot = null;
        Interlocked.Increment(ref _audioEligibilityVersion);
    }

    private HashSet<Element> GetTransitionParticipants()
    {
        int version = Volatile.Read(ref _participantsVersion);
        if (_participants is { } snapshot && snapshot.Version == version) return snapshot.Elements;

        HashSet<Element> elements = ElementTransitions.GetParticipants(Scene);
        _participants = new ParticipantSnapshot(version, elements);
        return elements;
    }

    private void OnSceneEdited(object? sender, EventArgs e)
    {
        Interlocked.Increment(ref _participantsVersion);
        Interlocked.Increment(ref _audioEligibilityVersion);
        _audioEligibility = null;
    }

    private void OnChildrenCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => OnSceneEdited(sender, e);

    private sealed record ParticipantSnapshot(int Version, HashSet<Element> Elements);

    private void OnLayerAttached(TimelineLayer layer)
    {
        ObserveLayer(layer);
        _layerSnapshot = null;
    }

    private void OnLayerDetached(TimelineLayer layer)
    {
        ForgetLayer(layer);
        _layerSnapshot = null;
    }

    private void ObserveLayer(TimelineLayer layer)
    {
        layer.ZIndex.ValueChanged += OnLayerZIndexChanged;
        layer.IsSolo.ValueChanged += OnLayerSoloChanged;
        layer.IsAudioMuted.ValueChanged += OnLayerAudioMutedChanged;
    }

    private void ForgetLayer(TimelineLayer layer)
    {
        layer.ZIndex.ValueChanged -= OnLayerZIndexChanged;
        layer.IsSolo.ValueChanged -= OnLayerSoloChanged;
        layer.IsAudioMuted.ValueChanged -= OnLayerAudioMutedChanged;
    }

    private void OnLayerZIndexChanged(object? sender, PropertyValueChangedEventArgs<int> e)
    {
        _layerSnapshot = null;
        Interlocked.Increment(ref _audioEligibilityVersion);
    }

    private void OnLayerSoloChanged(object? sender, PropertyValueChangedEventArgs<bool> e)
    {
        _layerSnapshot = null;
        Interlocked.Increment(ref _audioEligibilityVersion);
    }

    private void OnLayerAudioMutedChanged(object? sender, PropertyValueChangedEventArgs<bool> e)
        => Interlocked.Increment(ref _audioEligibilityVersion);

    public void Dispose()
    {
        DisposeQueryPools();
        Scene.Layers.CollectionChanged -= OnLayersCollectionChanged;
        Scene.Edited -= OnSceneEdited;
        Scene.Children.CollectionChanged -= OnChildrenCollectionChanged;
        Scene.Layers.Attached -= OnLayerAttached;
        Scene.Layers.Detached -= OnLayerDetached;
        foreach (TimelineLayer layer in Scene.Layers)
        {
            ForgetLayer(layer);
        }

        ReleaseCachedResources();
        _matteSources.Clear();
    }
}
