using System.Runtime.CompilerServices;
using Beutl.Collections.Pooled;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
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

    private sealed class CompositorContext(TimeSpan time, SceneCompositor compositor, IList<EngineObject.Resource> flow, IList<Element> currentElements)
        : RenderContext(time), ISceneCompositionRenderContext
    {
        public IList<EngineObject.Resource> Flow { get; set; } = flow;

        public IList<Element> CurrentElements { get; set; } = currentElements;

        public void EvaluateElementIntoFlow(Element element, EvaluationTarget target)
        {
            using var tmpObjects = new PooledList<EngineObject>();
            compositor.CollectResourcesFromElement(element, target, this, tmpObjects);
        }
    }

    public CompositionFrame Evaluate(TimeSpan time)
    {
        using var currentElements = new PooledList<Element>();
        SortLayers(time, currentElements);

        using var tmpObjects = new PooledList<EngineObject>();
        using var flow = new PooledList<EngineObject.Resource>();
        using var allResources = new PooledList<EngineObject.Resource>();
        var ctx = new CompositorContext(time, this, flow, currentElements);

        foreach (Element element in currentElements.Span)
        {
            flow.Clear();
            // EngineObjectを集める
            CollectResourcesFromElement(element, EvaluationTarget.Graphics, ctx, tmpObjects);

            allResources.AddRange(flow.Span);
        }

        return new CompositionFrame([.. allResources], time, _scene.FrameSize);
    }

    private void CollectResourcesFromElement(
        Element element, EvaluationTarget target,
        CompositorContext context, PooledList<EngineObject> tmpObjects)
    {
        // TODO: 分けるか分けないか
        using var flow = new PooledList<EngineObject.Resource>();
        var oldFlow = context.Flow;
        context.Flow = flow;
        try
        {
            tmpObjects.Clear();
            element.CollectObjects(target, tmpObjects);
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
    private void SortLayers(TimeSpan timeSpan, PooledList<Element> currentElements)
    {
        TimeSpan enterEnd = TimeSpan.Zero;

        foreach (Element? item in _scene.Children)
        {
            if (InRange(item, timeSpan))
            {
                currentElements.OrderedAdd(item, x => x.ZIndex);
            }
        }
    }

    // itemがtsの範囲内かを確かめます
    private static bool InRange(Element item, TimeSpan ts)
    {
        return item.Start <= ts && ts < item.Length + item.Start;
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
