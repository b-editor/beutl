using System.Diagnostics;
using System.Runtime.InteropServices;
using Beutl.Media.Source;
using Beutl.Threading;
using SkiaSharp;

namespace Beutl.Graphics.Rendering.V2.Cache;

public sealed class RenderNodeCache(RenderNode node) : IDisposable
{
    private readonly WeakReference<RenderNode> _node = new(node);
    private readonly List<(Ref<SKSurface>, Rect)> _cache = new(1);

    public const int Count = 3;

    private int _count;

    ~RenderNodeCache()
    {
        if (!IsDisposed)
            Dispose();
    }

    public bool IsCached => _cache.Count != 0;

    public int CacheCount => _cache.Count;

    public DateTime LastAccessedTime { get; private set; }

    public bool IsDisposed { get; private set; }

    public List<WeakReference<RenderNode>>? Children { get; private set; }

    public void ReportRenderCount(int count)
    {
        _count = count;
    }

    public void IncrementRenderCount()
    {
        _count++;
    }

    public void CaptureChildren()
    {
        if (_node.TryGetTarget(out RenderNode? node)
            && node is ContainerRenderNode container)
        {
            if (Children == null)
            {
                Children = new List<WeakReference<RenderNode>>(container.Children.Count);
            }
            else
            {
                Children.EnsureCapacity(container.Children.Count);
            }

            CollectionsMarshal.SetCount(Children, container.Children.Count);
            Span<WeakReference<RenderNode>> span = CollectionsMarshal.AsSpan(Children);

            for (int i = 0; i < container.Children.Count; i++)
            {
                RenderNode item = container.Children[i];
                ref WeakReference<RenderNode> refrence = ref span[i];

                if (refrence == null)
                {
                    refrence = new WeakReference<RenderNode>(item);
                }
                else
                {
                    refrence.SetTarget(item);
                }
            }
        }
    }

    public bool SameChildren()
    {
        if (Children != null
            && _node.TryGetTarget(out RenderNode? node)
            && node is ContainerRenderNode container)
        {
            if (Children.Count != container.Children.Count)
                return false;

            for (int i = 0; i < Children.Count; i++)
            {
                WeakReference<RenderNode> capturedRef = Children[i];
                RenderNode current = container.Children[i];
                if (!capturedRef.TryGetTarget(out RenderNode? captured)
                    || !ReferenceEquals(captured, current))
                {
                    return false;
                }
            }

            return true;
        }
        else
        {
            return true;
        }
    }

    public bool CanCache()
    {
        return _count >= Count;
    }

    public void Invalidate()
    {
        RenderThread.Dispatcher.CheckAccess();
#if DEBUG
        if (_cache.Count != 0)
        {
            Debug.WriteLine($"[RenderCache:Invalildated] '{(_node.TryGetTarget(out RenderNode? node) ? node : null)}'");
        }
#endif

        foreach ((Ref<SKSurface>, Rect) item in _cache)
        {
            item.Item1.Dispose();
        }
        _cache.Clear();
    }

    public void Dispose()
    {
        void DisposeOnRenderThread()
        {
            if (_cache.Count != 0)
            {
                (Ref<SKSurface>, Rect)[] tmp = [.. _cache];
                _cache.Clear();

                RenderThread.Dispatcher.Dispatch(() =>
                {
                    foreach ((Ref<SKSurface>, Rect) item in tmp)
                    {
                        item.Item1.Dispose();
                    }
                }, DispatchPriority.Low);
            }

            IsDisposed = true;
        }

        if (!IsDisposed)
        {
            if (RenderThread.Dispatcher.CheckAccess())
            {
                foreach ((Ref<SKSurface>, Rect) item in _cache)
                {
                    item.Item1.Dispose();
                }
                _cache.Clear();
                IsDisposed = true;
            }
            else
            {
                DisposeOnRenderThread();
            }

            GC.SuppressFinalize(this);
        }
    }

    public Ref<SKSurface> UseCache(out Rect bounds)
    {
        if (_cache.Count == 0)
        {
            throw new Exception("キャッシュはありません");
        }

        (Ref<SKSurface>, Rect) c = _cache[0];
        bounds = c.Item2;
        LastAccessedTime = DateTime.UtcNow;
        return c.Item1.Clone();
    }

    public void StoreCache(Ref<SKSurface> surface, Rect bounds)
    {
        Invalidate();

        _cache.Add((surface.Clone(), bounds));

        LastAccessedTime = DateTime.UtcNow;
    }

    public IEnumerable<(Ref<SKSurface> Surface, Rect Bounds)> UseCache()
    {
        LastAccessedTime = DateTime.UtcNow;
        return _cache.Select(i => (i.Item1.Clone(), i.Item2));
    }

    public void StoreCache(ReadOnlySpan<(Ref<SKSurface> Surface, Rect Bounds)> items)
    {
        Invalidate();

        foreach ((Ref<SKSurface> surface, Rect bounds) in items)
        {
            _cache.Add((surface.Clone(), bounds));
        }

        LastAccessedTime = DateTime.UtcNow;
    }
}
