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
    // One presenter per transition draws it for this compositor; it lives as long as the transition does.
    private readonly ConditionalWeakTable<ClipTransition, ClipTransitionPresenter> _transitionPresenters = new();

    // Mute flags are read live from the layers inside the snapshot, so only the
    // lookup shape (membership, ZIndex) and HasSolo require invalidation.
    private volatile LayerSnapshot? _layerSnapshot;
    // The elements that can take part in a transition, kept until the scene is edited. The version moves
    // on every edit, so a set built while an edit lands is not kept past it.
    private volatile ParticipantSnapshot? _participants;
    private int _participantsVersion;

    public SceneCompositor(Scene scene)
    {
        Scene = scene;
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
            CompositionTarget target) : base(time)
        {
            _compositor = compositor;
            CurrentElements = currentElements;
            Target = target;
            Flow = flow;
            DisableResourceShare = compositor.DisableResourceShare;
            PreferProxy = !compositor.ForceOriginalSource
                && GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode.Value == PreviewSourceMode.PreferProxy;
            PreferredProxyPreset = ToPreset(GlobalConfiguration.Instance.ProxyStoreConfig.DefaultPreset.Value);
            TargetDomain = new Rect(default, compositor.Scene.FrameSize.Value.ToSize(1));
            EvaluationServices = new EvaluationServices(compositor);
        }

        public IList<Element> CurrentElements { get; set; }

        public CompositionTarget Target { get; set; }

        // The frame's own time while a transition evaluates one of its sides at a held time. Elements a
        // portal draws into that side are not the side's own content and have not ended, so they play on
        // at the frame's time, and both sides share one evaluation of them.
        public MediaTime? FrameTime { get; set; }

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

    private CompositionFrame EvaluateGraphics(MediaTime time, int? layer, EvaluationServices? services)
    {
        DisposeDetachedResources();
        using var currentElements = new PooledList<Element>();
        SortLayers(time, currentElements, CompositionTarget.Graphics);

        using var tmpObjects = new PooledList<EngineObject>();
        using var flow = new PooledList<EngineObject.Resource>();
        using var allResources = new PooledList<EngineObject.Resource>();
        var ctx = new CompositorContext(time, this, flow, currentElements, CompositionTarget.Graphics);
        if (services is not null)
            ctx.EvaluationServices = services;

        // 途中でcurrentElementsが変わる可能性があるのでforループで回す
        for (int index = 0; index < currentElements.Count; index++)
        {
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
            ContainsScene3D = ctx.ContainsScene3D,
        };
        if (services is null)
            ((EvaluationServices)ctx.EvaluationServices!).Frame = frame;
        return frame;
    }

    public CompositionFrame EvaluateAudio(TimeRange timeRange)
    {
        DisposeDetachedResources();
        using var eligibleElements = new PooledList<Element>();
        LayerSnapshot snapshot = GetLayerSnapshot();
        foreach (Element item in Scene.Children)
        {
            if (!item.IsEnabled.Value) continue;
            if (ShouldSkipLayer(item.ZIndex.Value, CompositionTarget.Audio, snapshot.HasSolo, snapshot.ByZIndex)) continue;
            eligibleElements.OrderedAdd(item, x => x.ZIndex.Value);
        }

        using var currentElements = new PooledList<Element>();
        SortLayers(timeRange, currentElements, CompositionTarget.Audio);

        var activeElementSet = new HashSet<Element>(ReferenceEqualityComparer.Instance);
        foreach (Element element in currentElements)
        {
            activeElementSet.Add(element);
        }

        using var eligibleObjects = new PooledList<EngineObject>();
        foreach (Element element in eligibleElements)
        {
            if (!activeElementSet.Contains(element))
            {
                element.CollectObjects(CompositionTarget.Audio, eligibleObjects);
            }
        }

        using var tmpObjects = new PooledList<EngineObject>();
        using var flow = new PooledList<EngineObject.Resource>();
        using var allResources = new PooledList<EngineObject.Resource>();
        var ctx = new CompositorContext(timeRange.Start, this, flow, currentElements, CompositionTarget.Audio);

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
            new CompositionEligibility(eligibleObjects));
    }

    private void CollectResourcesFromElement(
        Element element, CompositorContext context, PooledList<EngineObject> tmpObjects)
    {
        if (context.Target == CompositionTarget.Graphics
            && ElementTransitions.TryGetActive(element, context.Time, out TransitionBoundary boundary))
        {
            CollectTransition(boundary, context);
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

            foreach (EngineObject.Resource resource in flow)
            {
                oldFlow?.Add(resource);
            }
        }
        finally
        {
            context.Flow = oldFlow;
        }
    }

    private sealed class ResourceEntry(EngineObject.Resource resource, EventHandler<HierarchyAttachmentEventArgs> detached)
    {
        public EngineObject.Resource Resource { get; set; } = resource;
        public EventHandler<HierarchyAttachmentEventArgs> Detached { get; } = detached;
    }

    private EngineObject.Resource GetOrCreateResource(EngineObject obj, CompositionContext context)
    {
        if (!_resourceCache.TryGetValue(obj, out var entry))
        {
            var resource = obj.ToResource(context);
            entry = new ResourceEntry(resource, AddDetachedHandler(obj));
            _resourceCache.Add(obj, entry);
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

    private EventHandler<HierarchyAttachmentEventArgs> AddDetachedHandler(EngineObject obj)
    {
        var weakRef = new WeakReference<SceneCompositor>(this);

        void Handler(object? sender, HierarchyAttachmentEventArgs e)
        {
            if (sender is not EngineObject senderObj) return;

            if (weakRef.TryGetTarget(out SceneCompositor? compositor))
            {
                lock (compositor._resourceCacheLock)
                {
                    if (compositor._resourceCache.TryGetValue(senderObj, out var entry))
                    {
                        compositor._resourceCache.Remove(senderObj);
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

    // timeに掛かるElementを、solo/muteでフィルタしつつZIndex順に振り分ける
    private void SortLayers(MediaTime time, PooledList<Element> currentElements, CompositionTarget target)
    {
        LayerSnapshot snapshot = GetLayerSnapshot();
        bool graphics = target == CompositionTarget.Graphics;
        foreach (Element item in Scene.Children)
        {
            if (!item.IsEnabled.Value || !CoversTime(item, time, graphics)) continue;
            if (ShouldSkipLayer(item.ZIndex.Value, target, snapshot.HasSolo, snapshot.ByZIndex)) continue;
            currentElements.OrderedAdd(item, x => x.ZIndex.Value);
        }

        if (graphics)
        {
            KeepOneElementPerTransition(time, currentElements);
        }
    }

    // Two elements that touch within the tolerance can leave a gap of up to that tolerance between them,
    // which their transition spans; a frame inside it still has to reach the transition through them.
    private static bool CoversTime(Element item, MediaTime time, bool graphics)
    {
        if (item.Range.Contains(time)) return true;
        if (!graphics) return false;

        MediaTime tolerance = ElementTransitions.AdjacencyTolerance;
        return time >= item.Start.Value - tolerance
               && time < item.Range.End + tolerance
               && ElementTransitions.TryGetActive(item, time, out _);
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
        LayerSnapshot snapshot = GetLayerSnapshot();
        foreach (Element item in Scene.Children)
        {
            if (!item.IsEnabled.Value || !item.Range.Intersects(timeRange)) continue;
            if (ShouldSkipLayer(item.ZIndex.Value, target, snapshot.HasSolo, snapshot.ByZIndex)) continue;
            currentElements.OrderedAdd(item, x => x.ZIndex.Value);
        }
    }

    private sealed record LayerSnapshot(Dictionary<int, TimelineLayer> ByZIndex, bool HasSolo);

    private static readonly LayerSnapshot s_emptyLayerSnapshot = new([], false);

    // Concurrent rebuilds after an invalidation are benign: each produces an
    // equivalent snapshot and the last write wins.
    private LayerSnapshot GetLayerSnapshot()
    {
        LayerSnapshot? snapshot = _layerSnapshot;
        if (snapshot is not null) return snapshot;

        if (Scene.Layers.Count == 0)
        {
            _layerSnapshot = s_emptyLayerSnapshot;
            return s_emptyLayerSnapshot;
        }

        var byZIndex = new Dictionary<int, TimelineLayer>(Scene.Layers.Count);
        bool hasSolo = false;
        foreach (TimelineLayer layer in Scene.Layers)
        {
            if (byZIndex.TryAdd(layer.ZIndex.Value, layer) && layer.IsSolo.Value)
            {
                hasSolo = true;
            }
        }

        snapshot = new LayerSnapshot(byZIndex, hasSolo);
        _layerSnapshot = snapshot;
        return snapshot;
    }

    private void OnLayersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => _layerSnapshot = null;

    private HashSet<Element> GetTransitionParticipants()
    {
        int version = Volatile.Read(ref _participantsVersion);
        if (_participants is { } snapshot && snapshot.Version == version) return snapshot.Elements;

        HashSet<Element> elements = ElementTransitions.GetParticipants(Scene);
        _participants = new ParticipantSnapshot(version, elements);
        return elements;
    }

    private void OnSceneEdited(object? sender, EventArgs e)
        => Interlocked.Increment(ref _participantsVersion);

    private void OnChildrenCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Interlocked.Increment(ref _participantsVersion);

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
    }

    private void ForgetLayer(TimelineLayer layer)
    {
        layer.ZIndex.ValueChanged -= OnLayerZIndexChanged;
        layer.IsSolo.ValueChanged -= OnLayerSoloChanged;
    }

    private void OnLayerZIndexChanged(object? sender, PropertyValueChangedEventArgs<int> e) => _layerSnapshot = null;

    private void OnLayerSoloChanged(object? sender, PropertyValueChangedEventArgs<bool> e) => _layerSnapshot = null;

    // A layer without a TimelineLayer model cannot be soloed, so it is excluded
    // under solo mode. Mute is independent per target (audio vs video).
    private static bool ShouldSkipLayer(
        int zIndex,
        CompositionTarget target,
        bool hasSolo,
        Dictionary<int, TimelineLayer> layersByZIndex)
    {
        layersByZIndex.TryGetValue(zIndex, out TimelineLayer? layer);
        if (hasSolo && (layer is null || !layer.IsSolo.Value)) return true;
        if (layer is null) return false;
        return target == CompositionTarget.Graphics ? layer.IsVideoMuted.Value : layer.IsAudioMuted.Value;
    }

    public void Dispose()
    {
        Scene.Layers.CollectionChanged -= OnLayersCollectionChanged;
        Scene.Edited -= OnSceneEdited;
        Scene.Children.CollectionChanged -= OnChildrenCollectionChanged;
        Scene.Layers.Attached -= OnLayerAttached;
        Scene.Layers.Detached -= OnLayerDetached;
        foreach (TimelineLayer layer in Scene.Layers)
        {
            ForgetLayer(layer);
        }

        lock (_resourceCacheLock)
        {
            foreach (var kvp in _resourceCache)
            {
                kvp.Key.DetachedFromHierarchy -= kvp.Value.Detached;
                _detachedResources.Enqueue(kvp.Value.Resource);
            }

            _resourceCache.Clear();
        }

        // The cache is empty and all earlier detachments have enqueued their resources. Release them
        // outside the lock because resource disposal can itself trigger hierarchy callbacks.
        DisposeDetachedResources();
    }
}
