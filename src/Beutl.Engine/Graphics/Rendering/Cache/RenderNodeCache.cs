using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering.Cache;

public sealed class RenderNodeCache(RenderNode node) : IDisposable
{
    private readonly WeakReference<RenderNode> _node = new(node);
    private readonly List<(RenderTarget, Rect)> _cache = new(1);

    public const int Count = 3;

    private int _count;

    // Set when CreateDefaultCache refuses this subtree (a supply density above outputScale, or an over-budget
    // tile). The render count keeps climbing and the subtree stays uncached, so without this flag MakeCache would
    // re-pull and re-reject it every frame. Cleared when the node changes (IncrementRenderCount) or the cache is
    // invalidated, so a fresh attempt can happen once the subtree re-warms.
    private bool _cacheRejected;

    ~RenderNodeCache()
    {
        if (!IsDisposed)
            Dispose();
    }

    public bool IsCached => _cache.Count != 0;

    public int CacheCount => _cache.Count;

    /// <summary>
    /// The pixel density (device px per logical unit) the cached tiles were rasterized at (FR-020).
    /// Replay re-tags the tiles <c>EffectiveScale.At(Density)</c> so a cached subtree keeps its true
    /// supply density instead of flipping to <c>Unbounded</c>, which would change downstream working
    /// scales when the cache is enabled. Cross-scale cache reuse is out of scope (T025); density is
    /// fixed within one renderer.
    /// </summary>
    public float Density { get; private set; } = 1f;

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
            _cacheRejected = false;
        }
    }

    public bool CanCache()
    {
        return _count >= Count;
    }

    /// <summary>
    /// True once <see cref="RenderNodeCacheHelper.CreateDefaultCache"/> has refused to cache this subtree, so the
    /// per-frame <see cref="RenderNodeCacheHelper.MakeCache"/> stops re-pulling and re-rejecting it. Reset by
    /// <see cref="IncrementRenderCount"/> when the node changes and by <see cref="Invalidate"/>.
    /// </summary>
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

        LastAccessedTime = DateTime.UtcNow;
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
        LastAccessedTime = DateTime.UtcNow;
    }
}
