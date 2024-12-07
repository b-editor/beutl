using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

        public bool IsDirty { get; set; } = true;

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

    public void Add(Drawable drawable)
    {
        if (!_cache.TryGetValue(drawable, out Entry? entry))
        {
            entry = new Entry(new DrawableRenderNode(drawable));
            _cache.Add(drawable, entry);

            var weakRef = new WeakReference<Entry>(entry);
            EventHandler<RenderInvalidatedEventArgs>? handler = null;
            handler = (_, _) =>
            {
                if (weakRef.TryGetTarget(out Entry? obj))
                {
                    obj.IsDirty = true;
                }
                else
                {
                    drawable.Invalidated -= handler;
                }
            };
            drawable.Invalidated += handler;
        }

        if (entry.IsDirty)
        {
            // DeferredCanvasを作成し、記録
            using var canvas = new GraphicsContext2D(entry.Node, renderScene.Size);
            drawable.Render(canvas);
            entry.IsDirty = false;
        }

        CurrentFrame.Add(entry);
    }

    public void UpdateAll(IReadOnlyList<Drawable> elements)
    {
        _currentFrame?.Clear();
        if (elements.Count == 0)
        {
            return;
        }

        CurrentFrame.EnsureCapacity(elements.Count);

        foreach (Drawable element in elements)
        {
            Add(element);
        }
    }

    public void ClearAllNodeCache(RenderNodeCacheContext? context)
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
            Drawable drawable = node.Drawable;
            if (entry.IsDirty)
            {
                using var context = new GraphicsContext2D(node, renderScene.Size);
                drawable.Render(context);
                entry.IsDirty = false;
            }

            RenderNodeCacheContext? cacheContext = canvas.GetCacheContext();
            if (cacheContext != null)
            {
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

                RevalidateAll(node);
            }

            var processor = new RenderNodeProcessor(node, canvas, true);
            Rect bounds = default;
            var ops = processor.PullToRoot();
            foreach (var op in ops)
            {
                op.Render(canvas);
                op.Dispose();
                bounds = bounds.Union(op.Bounds);
            }

            entry.Bounds = bounds;

            cacheContext?.MakeCache(node, canvas);
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
            // TODO: HitTestの実装
            // if (entry.Node.HitTest(point))
            // {
            //     return entry.Node.Drawable;
            // }
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
