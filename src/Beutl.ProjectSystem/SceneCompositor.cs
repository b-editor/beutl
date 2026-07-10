using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Beutl.Collections.Pooled;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed class SceneCompositor : ICompositor
{
    private readonly ConditionalWeakTable<EngineObject, EngineObject.Resource> _resourceCache = new();

    // Mute flags are read live from the layers inside the snapshot, so only the
    // lookup shape (membership, ZIndex) and HasSolo require invalidation.
    private volatile LayerSnapshot? _layerSnapshot;

    public SceneCompositor(Scene scene)
    {
        Scene = scene;
        Scene.Layers.CollectionChanged += OnLayersCollectionChanged;
        Scene.Layers.Attached += OnLayerAttached;
        Scene.Layers.Detached += OnLayerDetached;
        foreach (TimelineLayer layer in Scene.Layers)
        {
            layer.PropertyChanged += OnLayerPropertyChanged;
        }
    }

    public Scene Scene { get; }

    public bool DisableResourceShare { get; init; }

    private sealed class CompositorContext : CompositionContext, ISceneCompositionContext
    {
        private readonly SceneCompositor _compositor;

        public CompositorContext(TimeSpan time,
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
        }

        public IList<Element> CurrentElements { get; set; }

        public CompositionTarget Target { get; set; }

        public void EvaluateElementIntoFlow(Element element)
        {
            using var tmpObjects = new PooledList<EngineObject>();
            _compositor.CollectResourcesFromElement(element, this, tmpObjects);
        }
    }

    public CompositionFrame EvaluateGraphics(TimeSpan time)
    {
        using var currentElements = new PooledList<Element>();
        SortLayers(time, currentElements, CompositionTarget.Graphics);

        using var tmpObjects = new PooledList<EngineObject>();
        using var flow = new PooledList<EngineObject.Resource>();
        using var allResources = new PooledList<EngineObject.Resource>();
        var ctx = new CompositorContext(time, this, flow, currentElements, CompositionTarget.Graphics);

        // 途中でcurrentElementsが変わる可能性があるのでforループで回す
        for (int index = 0; index < currentElements.Count; index++)
        {
            flow.Clear();
            // EngineObjectを集める
            CollectResourcesFromElement(currentElements[index], ctx, tmpObjects);

            allResources.AddRange(flow.Span);
        }

        return new CompositionFrame([.. allResources], new(time, TimeSpan.FromTicks(1)), Scene.FrameSize);
    }

    public CompositionFrame EvaluateAudio(TimeRange timeRange)
    {
        using var currentElements = new PooledList<Element>();
        SortLayers(timeRange, currentElements, CompositionTarget.Audio);

        using var tmpObjects = new PooledList<EngineObject>();
        using var flow = new PooledList<EngineObject.Resource>();
        using var allResources = new PooledList<EngineObject.Resource>();
        var ctx = new CompositorContext(timeRange.Start, this, flow, currentElements, CompositionTarget.Audio);

        // 途中でcurrentElementsが変わる可能性があるのでforループで回す
        for (int index = 0; index < currentElements.Count; index++)
        {
            flow.Clear();
            // EngineObjectを集める
            CollectResourcesFromElement(currentElements[index], ctx, tmpObjects);

            allResources.AddRange(flow.Span);
        }

        return new CompositionFrame([.. allResources], timeRange, Scene.FrameSize);
    }

    private void CollectResourcesFromElement(
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

    private EngineObject.Resource GetOrCreateResource(EngineObject obj, CompositionContext context)
    {
        if (!_resourceCache.TryGetValue(obj, out var resource) || resource.IsDisposed)
        {
            AddDetachedHandler(obj);
            resource = obj.ToResource(context);
            _resourceCache.AddOrUpdate(obj, resource);
        }
        else
        {
            bool _ = false;
            resource.Update(obj, context, ref _);
        }

        return resource;
    }

    private void AddDetachedHandler(EngineObject obj)
    {
        var weakRef = new WeakReference<SceneCompositor>(this);

        void Handler(object? sender, HierarchyAttachmentEventArgs e)
        {
            if (sender is not EngineObject senderObj) return;

            if (weakRef.TryGetTarget(out SceneCompositor? compositor)
                && compositor._resourceCache.TryGetValue(senderObj, out var resource))
            {
                resource.Dispose();
                compositor._resourceCache.Remove(senderObj);
            }

            senderObj.DetachedFromHierarchy -= Handler;
        }

        obj.DetachedFromHierarchy += Handler;
    }

    // timeに掛かるElementを、solo/muteでフィルタしつつZIndex順に振り分ける
    private void SortLayers(TimeSpan time, PooledList<Element> currentElements, CompositionTarget target)
    {
        LayerSnapshot snapshot = GetLayerSnapshot();
        if (snapshot.ByZIndex.Count == 0)
        {
            foreach (Element item in Scene.Children)
            {
                if (item.IsEnabled && item.Range.Contains(time))
                {
                    currentElements.OrderedAdd(item, x => x.ZIndex);
                }
            }

            return;
        }

        foreach (Element item in Scene.Children)
        {
            if (!item.IsEnabled || !item.Range.Contains(time)) continue;
            if (ShouldSkipLayer(item.ZIndex, target, snapshot.HasSolo, snapshot.ByZIndex)) continue;
            currentElements.OrderedAdd(item, x => x.ZIndex);
        }
    }

    // timeRangeに掛かるElementを、solo/muteでフィルタしつつZIndex順に振り分ける
    private void SortLayers(TimeRange timeRange, PooledList<Element> currentElements, CompositionTarget target)
    {
        LayerSnapshot snapshot = GetLayerSnapshot();
        if (snapshot.ByZIndex.Count == 0)
        {
            foreach (Element item in Scene.Children)
            {
                if (item.IsEnabled && item.Range.Intersects(timeRange))
                {
                    currentElements.OrderedAdd(item, x => x.ZIndex);
                }
            }

            return;
        }

        foreach (Element item in Scene.Children)
        {
            if (!item.IsEnabled || !item.Range.Intersects(timeRange)) continue;
            if (ShouldSkipLayer(item.ZIndex, target, snapshot.HasSolo, snapshot.ByZIndex)) continue;
            currentElements.OrderedAdd(item, x => x.ZIndex);
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
            if (byZIndex.TryAdd(layer.ZIndex, layer) && layer.IsSolo)
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

    private void OnLayerAttached(TimelineLayer layer)
    {
        layer.PropertyChanged += OnLayerPropertyChanged;
        _layerSnapshot = null;
    }

    private void OnLayerDetached(TimelineLayer layer)
    {
        layer.PropertyChanged -= OnLayerPropertyChanged;
        _layerSnapshot = null;
    }

    private void OnLayerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TimelineLayer.ZIndex) or nameof(TimelineLayer.IsSolo))
        {
            _layerSnapshot = null;
        }
    }

    // A layer without a TimelineLayer model cannot be soloed, so it is excluded
    // under solo mode. Mute is independent per target (audio vs video).
    private static bool ShouldSkipLayer(
        int zIndex,
        CompositionTarget target,
        bool hasSolo,
        Dictionary<int, TimelineLayer> layersByZIndex)
    {
        layersByZIndex.TryGetValue(zIndex, out TimelineLayer? layer);
        if (hasSolo && (layer is null || !layer.IsSolo)) return true;
        if (layer is null) return false;
        return target == CompositionTarget.Graphics ? layer.IsVideoMuted : layer.IsAudioMuted;
    }

    public void Dispose()
    {
        Scene.Layers.CollectionChanged -= OnLayersCollectionChanged;
        Scene.Layers.Attached -= OnLayerAttached;
        Scene.Layers.Detached -= OnLayerDetached;
        foreach (TimelineLayer layer in Scene.Layers)
        {
            layer.PropertyChanged -= OnLayerPropertyChanged;
        }

        foreach (var kvp in _resourceCache)
        {
            kvp.Value.Dispose();
        }

        _resourceCache.Clear();
    }
}
