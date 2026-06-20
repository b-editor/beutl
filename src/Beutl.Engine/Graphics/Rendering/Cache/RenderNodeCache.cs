using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering.Cache;

public sealed class RenderNodeCache(RenderNode node) : IDisposable
{
    private readonly WeakReference<RenderNode> _node = new(node);
    private readonly List<(RenderTarget, Rect)> _cache = new(1);

    public const int Count = 3;

    private int _count;

    // Set when CreateDefaultCache refuses this subtree; cleared on node change or invalidation.
    private bool _cacheRejected;

    ~RenderNodeCache()
    {
        if (!IsDisposed)
            Dispose();
    }

    public bool IsCached => _cache.Count != 0;

    public int CacheCount => _cache.Count;

    /// <summary>
    /// The pixel density the cached tiles were rasterized at. Replay re-tags tiles at this density.
    /// </summary>
    public float Density { get; private set; } = 1f;

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
            _cacheRejected = false;
        }
    }

    public bool CanCache()
    {
        return _count >= Count;
    }

    /// <summary>True once cache creation was refused; stops re-attempts each frame.</summary>
    public bool IsCacheRejected => _cacheRejected;

    public void RejectCache()
    {
        _cacheRejected = true;
    }

    public void Invalidate()
    {
        if (_cache.Count != 0)
        {
            RenderNodeCacheHelper._logger.LogInformation("Invalidating Cache for {Node}",
                _node.TryGetTarget(out RenderNode? node) ? node : null);
        }

        foreach ((RenderTarget, Rect) item in _cache)
        {
            item.Item1.Dispose();
        }

        _cache.Clear();
        _cacheRejected = false;
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
        return c.Item1.ShallowCopy();
    }

    public void StoreCache(RenderTarget renderTarget, Rect bounds, float density = 1f)
    {
        Invalidate();

        _cache.Add((renderTarget.ShallowCopy(), bounds));
        Density = density;
    }

    public IEnumerable<(RenderTarget RenderTarget, Rect Bounds)> UseCache()
    {
        return _cache.Select(i => (i.Item1.ShallowCopy(), i.Item2));
    }

    public void StoreCache(ReadOnlySpan<(RenderTarget RenderTarget, Rect Bounds)> items, float density = 1f)
    {
        Invalidate();

        foreach ((RenderTarget renderTarget, Rect bounds) in items)
        {
            _cache.Add((renderTarget.ShallowCopy(), bounds));
        }

        Density = density;
    }
}
