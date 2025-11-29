using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering.Cache;

public sealed class RenderNodeCache(RenderNode node) : IDisposable
{
    private readonly WeakReference<RenderNode> _node = new(node);
    private readonly List<(RenderTarget, Rect)> _cache = new(1);

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

    public void ReportRenderCount(int count)
    {
        _count = count;
    }

    public void IncrementRenderCount()
    {
        if (_node.TryGetTarget(out RenderNode? node) && !node.HasChanges)
        {
            _count++;
        }
        else
        {
            _count = 0;
        }
    }

    public bool CanCache()
    {
        return _count >= Count;
    }

    public void Invalidate()
    {
        if (_cache.Count != 0)
        {
            RenderNodeCacheContext._logger.LogInformation("Invalidating Cache for {Node}",
                _node.TryGetTarget(out RenderNode? node) ? node : null);
        }

        foreach ((RenderTarget, Rect) item in _cache)
        {
            item.Item1.Dispose();
        }

        _cache.Clear();
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            foreach ((RenderTarget, Rect) item in _cache)
            {
                item.Item1.Dispose();
            }

            _cache.Clear();
            IsDisposed = true;

            GC.SuppressFinalize(this);
        }
    }

    public RenderTarget UseCache(out Rect bounds)
    {
        if (_cache.Count == 0)
        {
            throw new Exception("キャッシュはありません");
        }

        (RenderTarget, Rect) c = _cache[0];
        bounds = c.Item2;
        LastAccessedTime = DateTime.UtcNow;
        return c.Item1.ShallowCopy();
    }

    public void StoreCache(RenderTarget renderTarget, Rect bounds)
    {
        Invalidate();

        _cache.Add((renderTarget.ShallowCopy(), bounds));

        LastAccessedTime = DateTime.UtcNow;
    }

    public IEnumerable<(RenderTarget RenderTarget, Rect Bounds)> UseCache()
    {
        LastAccessedTime = DateTime.UtcNow;
        return _cache.Select(i => (i.Item1.ShallowCopy(), i.Item2));
    }

    public void StoreCache(ReadOnlySpan<(RenderTarget RenderTarget, Rect Bounds)> items)
    {
        Invalidate();

        foreach ((RenderTarget renderTarget, Rect bounds) in items)
        {
            _cache.Add((renderTarget.ShallowCopy(), bounds));
        }

        LastAccessedTime = DateTime.UtcNow;
    }
}
