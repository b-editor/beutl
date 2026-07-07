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

    public SceneCompositor(Scene scene)
    {
        Scene = scene;
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

    // Layersを振り分ける
    private void SortLayers(TimeSpan time, PooledList<Element> currentElements, CompositionTarget target)
        => SortLayersCore(currentElements, target, item => item.Range.Contains(time));

    private void SortLayers(TimeRange timeRange, PooledList<Element> currentElements, CompositionTarget target)
        => SortLayersCore(currentElements, target, item => item.Range.Intersects(timeRange));

    private void SortLayersCore(
        PooledList<Element> currentElements,
        CompositionTarget target,
        Func<Element, bool> isActive)
    {
        Dictionary<int, TimelineLayer> layersByZIndex = CreateLayerLookup();
        bool hasSolo = AnySoloLayer(layersByZIndex);
        foreach (Element item in Scene.Children)
        {
            if (!item.IsEnabled || !isActive(item)) continue;
            if (ShouldSkipLayer(item.ZIndex, target, hasSolo, layersByZIndex)) continue;
            currentElements.OrderedAdd(item, x => x.ZIndex);
        }
    }

    private Dictionary<int, TimelineLayer> CreateLayerLookup()
    {
        var layersByZIndex = new Dictionary<int, TimelineLayer>(Scene.Layers.Count);
        foreach (TimelineLayer layer in Scene.Layers)
        {
            if (!layersByZIndex.ContainsKey(layer.ZIndex))
            {
                layersByZIndex.Add(layer.ZIndex, layer);
            }
        }

        return layersByZIndex;
    }

    private static bool AnySoloLayer(Dictionary<int, TimelineLayer> layersByZIndex)
    {
        foreach (TimelineLayer layer in layersByZIndex.Values)
        {
            if (layer.IsSolo) return true;
        }
        return false;
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
        foreach (var kvp in _resourceCache)
        {
            kvp.Value.Dispose();
        }

        _resourceCache.Clear();
    }
}
