using System.Runtime.CompilerServices;
using Beutl.Collections.Pooled;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed class SceneCompositor : ICompositor
{
    private readonly Scene _scene;
    private readonly ConditionalWeakTable<EngineObject, EngineObject.Resource> _resourceCache = new();

    public SceneCompositor(Scene scene)
    {
        _scene = scene;
    }

    private sealed class CompositorContext(
        TimeSpan time,
        SceneCompositor compositor,
        IList<EngineObject.Resource> flow,
        IList<Element> currentElements,
        EvaluationTarget target)
        : RenderContext(time), ISceneCompositionRenderContext
    {
        public IList<EngineObject.Resource> Flow { get; set; } = flow;

        public IList<Element> CurrentElements { get; set; } = currentElements;

        public EvaluationTarget Target { get; set; } = target;

        public void EvaluateElementIntoFlow(Element element)
        {
            using var tmpObjects = new PooledList<EngineObject>();
            compositor.CollectResourcesFromElement(element, this, tmpObjects);
        }
    }

    public CompositionFrame EvaluateGraphics(TimeSpan time)
    {
        using var currentElements = new PooledList<Element>();
        SortLayers(time, currentElements);

        using var tmpObjects = new PooledList<EngineObject>();
        using var flow = new PooledList<EngineObject.Resource>();
        using var allResources = new PooledList<EngineObject.Resource>();
        var ctx = new CompositorContext(time, this, flow, currentElements, EvaluationTarget.Graphics);

        // 途中でcurrentElementsが変わる可能性があるのでforループで回す
        for (int index = 0; index < currentElements.Count; index++)
        {
            flow.Clear();
            // EngineObjectを集める
            CollectResourcesFromElement(currentElements[index], ctx, tmpObjects);

            allResources.AddRange(flow.Span);
        }

        return new CompositionFrame([.. allResources], new(time, TimeSpan.FromTicks(1)), _scene.FrameSize);
    }

    public CompositionFrame EvaluateAudio(TimeRange timeRange)
    {
        using var currentElements = new PooledList<Element>();
        SortLayers(timeRange, currentElements);

        using var tmpObjects = new PooledList<EngineObject>();
        using var flow = new PooledList<EngineObject.Resource>();
        using var allResources = new PooledList<EngineObject.Resource>();
        var ctx = new CompositorContext(timeRange.Start, this, flow, currentElements, EvaluationTarget.Audio);

        // 途中でcurrentElementsが変わる可能性があるのでforループで回す
        for (int index = 0; index < currentElements.Count; index++)
        {
            flow.Clear();
            // EngineObjectを集める
            CollectResourcesFromElement(currentElements[index], ctx, tmpObjects);

            allResources.AddRange(flow.Span);
        }

        return new CompositionFrame([.. allResources], timeRange, _scene.FrameSize);
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
                oldFlow.Add(resource);
            }
        }
        finally
        {
            context.Flow = oldFlow;
        }
    }

    private EngineObject.Resource GetOrCreateResource(EngineObject obj, RenderContext context)
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
    private void SortLayers(TimeSpan time, PooledList<Element> currentElements)
    {
        foreach (Element item in _scene.Children)
        {
            if (item.Range.Contains(time))
            {
                currentElements.OrderedAdd(item, x => x.ZIndex);
            }
        }
    }

    private void SortLayers(TimeRange timeRange, PooledList<Element> currentElements)
    {
        foreach (Element item in _scene.Children)
        {
            if (item.Range.Intersects(timeRange))
            {
                currentElements.OrderedAdd(item, x => x.ZIndex);
            }
        }
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
