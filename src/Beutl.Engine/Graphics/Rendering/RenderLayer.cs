using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Beutl.Animation;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class RenderLayer(RenderScene renderScene) : IDisposable
{
    private class Entry(DrawableRenderNode node) : IDisposable
    {
        ~Entry()
        {
            Dispose();
        }

        public DrawableRenderNode Node { get; } = node;

        public Rect Bounds { get; set; }

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            if (!IsDisposed)
            {
                Node.Dispose();
                IsDisposed = true;
                GC.SuppressFinalize(this);
            }
        }
    }

    // このテーブルは本描画するときに、自分のレイヤー以外のものを削除する。
    private readonly ConditionalWeakTable<Drawable, Entry> _cache = [];
    private List<Entry>? _currentFrame;

    private List<Entry> CurrentFrame => _currentFrame ??= new(1);

    public void Clear()
    {
        _currentFrame?.Clear();
    }

    // Drawable
    public void Add(Drawable drawable, TimeSpan time)
    {
        var renderContext = new RenderContext(time);
        Drawable.Resource? resource;
        bool shouldRender;
        if (!_cache.TryGetValue(drawable, out Entry? entry))
        {
            resource = drawable.ToResource(renderContext);
            entry = new Entry(new DrawableRenderNode(resource));
            _cache.Add(drawable, entry);
            shouldRender = true;
        }
        else
        {
            resource = entry.Node.Drawable!.Value.Resource;
            bool updated = false;
            resource.Update(drawable, renderContext, ref updated);
            shouldRender = entry.Node.Update(resource);
        }

        if (shouldRender)
        {
            using var canvas = new GraphicsContext2D(entry.Node, renderScene.Size);
            drawable.Render(canvas, resource);
        }

        CurrentFrame.Add(entry);
    }

    public void UpdateAll(IReadOnlyList<Drawable> elements, TimeSpan time)
    {
        _currentFrame?.Clear();
        if (elements.Count == 0)
        {
            return;
        }

        CurrentFrame.EnsureCapacity(elements.Count);

        foreach (Drawable element in elements)
        {
            Add(element, time);
        }
    }

    public void ClearAllNodeCache()
    {
        foreach (KeyValuePair<Drawable, Entry> item in _cache)
        {
            RenderNodeCacheContext.ClearCache(item.Value.Node);

            item.Value.Dispose();
        }

        _cache.Clear();
    }

    public void Render(ImmediateCanvas canvas)
    {
        foreach (Entry? entry in CollectionsMarshal.AsSpan(_currentFrame))
        {
            DrawableRenderNode node = entry.Node;

            var cacheContext = renderScene._cacheContext;

            RevalidateAll(node);

            var processor = new RenderNodeProcessor(node, true);
            Rect bounds = default;
            var ops = processor.PullToRoot();
            foreach (var op in ops)
            {
                op.Render(canvas);
                op.Dispose();
                bounds = bounds.Union(op.Bounds);
            }

            entry.Bounds = bounds;

            cacheContext.MakeCache(node);
            continue;

            void RevalidateAll(RenderNode current)
            {
                RenderNodeCache cache = current.Cache;

                if (current is ContainerRenderNode c)
                {
                    foreach (RenderNode item in c.Children)
                    {
                        RevalidateAll(item);
                    }

                    cache.CaptureChildren();
                }

                cache.IncrementRenderCount();
                if (cache.IsCached && !RenderNodeCacheContext.CanCacheRecursive(current))
                {
                    cache.Invalidate();
                }
            }
        }
    }

    public void Dispose()
    {
        foreach (KeyValuePair<Drawable, Entry> item in _cache)
        {
            item.Value.Dispose();
        }

        _cache.Clear();
        if (_currentFrame != null)
        {
            foreach (Entry item in _currentFrame)
            {
                item.Dispose();
            }

            _currentFrame.Clear();
        }
    }

    public Drawable? HitTest(Point point)
    {
        if (_currentFrame == null || _currentFrame.Count == 0)
            return null;

        for (int i = _currentFrame.Count - 1; i >= 0; i--)
        {
            Entry entry = _currentFrame[i];
            var processor = new RenderNodeProcessor(entry.Node, false);
            var arr = processor.PullToRoot();
            try
            {
                if (arr.Any(op => op.HitTest(point)))
                {
                    return entry.Node.Drawable?.Resource.GetOriginal();
                }
            }
            finally
            {
                foreach (var op in arr)
                {
                    op.Dispose();
                }
            }
        }

        return null;
    }

    public Rect[] GetBoundaries()
    {
        if (_currentFrame == null || _currentFrame.Count == 0)
            return [];

        var list = new Rect[_currentFrame.Count];
        int index = 0;
        foreach (Entry? entry in CollectionsMarshal.AsSpan(_currentFrame))
        {
            list[index++] = entry.Bounds;
            //list[index++] = node.Drawable.Bounds;
        }

        return list;
    }

    internal void ClearCache()
    {
        foreach (KeyValuePair<Drawable, Entry> item in _cache)
        {
            RenderNodeCacheContext.ClearCache(item.Value.Node);
        }

        if (_currentFrame == null)
            return;

        foreach (Entry item in _currentFrame)
        {
            RenderNodeCacheContext.ClearCache(item.Node);
        }
    }
}
