using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering.Cache;

public sealed class RenderNodeCache(RenderNode node) : IDisposable
{
    private readonly WeakReference<RenderNode> _node = new(node);
    private List<(RenderTarget, Rect)> _cache = new(1);
    private readonly HashSet<(RenderIntent RenderIntent, RenderPullPurpose PullPurpose)> _rejectedPolicies = [];
    private RenderIntent? _cachedRenderIntent;
    private RenderPullPurpose? _cachedPullPurpose;

    /// <remarks><c>PrefixOutputCache.EngageThreshold</c> mirrors this value so pass-prefix caching engages on the
    /// same stable-frame count as node-tile caching.</remarks>
    public const int Count = 3;

    private int _count;

    ~RenderNodeCache()
    {
        if (!IsDisposed)
        {
            try
            {
                Dispose();
            }
            catch
            {
                // Finalizers must never allow cleanup failures to escape onto the finalizer thread.
            }
        }
    }

    public bool IsCached => _cache.Count != 0;

    /// <summary>The render intent that produced the retained tiles, or <see langword="null"/> when empty.</summary>
    public RenderIntent? CachedRenderIntent => IsCached ? _cachedRenderIntent : null;

    /// <summary>The pull purpose that produced the retained tiles, or <see langword="null"/> when empty.</summary>
    public RenderPullPurpose? CachedPullPurpose => IsCached ? _cachedPullPurpose : null;

    public int CacheCount => _cache.Count;

    /// <summary>
    /// The pixel density the cached tiles were rasterized at. Replay re-tags tiles at this density.
    /// </summary>
    public float Density { get; private set; } = 1f;

    public bool IsDisposed { get; private set; }

    public void ReportRenderCount(int count)
    {
        ThrowIfDisposed();
        _count = count;
    }

    public void IncrementRenderCount()
    {
        ThrowIfDisposed();
        if (_node.TryGetTarget(out RenderNode? node) && !node.HasChanges)
        {
            _count++;
        }
        else
        {
            _count = 0;
            Invalidate();
        }
    }

    public bool CanCache()
    {
        ThrowIfDisposed();
        return _count >= Count;
    }

    /// <summary>True when cache creation was refused for at least one frame-render policy.</summary>
    public bool IsCacheRejected => _rejectedPolicies.Count != 0;

    /// <summary>Returns whether retained tiles were produced under the exact policy.</summary>
    public bool IsCachedFor(RenderIntent renderIntent, RenderPullPurpose pullPurpose = RenderPullPurpose.Frame)
    {
        renderIntent = RenderPolicyValidation.Validate(renderIntent, nameof(renderIntent));
        pullPurpose = RenderPolicyValidation.Validate(pullPurpose, nameof(pullPurpose));
        return pullPurpose == RenderPullPurpose.Frame
            && IsCached
            && _cachedRenderIntent == renderIntent
            && _cachedPullPurpose == pullPurpose;
    }

    /// <summary>Returns whether cache creation was refused under the exact policy.</summary>
    public bool IsCacheRejectedFor(
        RenderIntent renderIntent,
        RenderPullPurpose pullPurpose = RenderPullPurpose.Frame)
    {
        renderIntent = RenderPolicyValidation.Validate(renderIntent, nameof(renderIntent));
        pullPurpose = RenderPolicyValidation.Validate(pullPurpose, nameof(pullPurpose));
        return _rejectedPolicies.Contains((renderIntent, pullPurpose));
    }

    /// <summary>
    /// Marks both preview and delivery frame policies rejected. This preserves the original policy-agnostic API;
    /// policy-aware cache producers should use <see cref="RejectCache(RenderIntent, RenderPullPurpose)"/>.
    /// </summary>
    public void RejectCache()
    {
        ThrowIfDisposed();
        _rejectedPolicies.Add((RenderIntent.Preview, RenderPullPurpose.Frame));
        _rejectedPolicies.Add((RenderIntent.Delivery, RenderPullPurpose.Frame));
    }

    /// <summary>Marks persistent cache creation rejected for one frame-render policy.</summary>
    public void RejectCache(
        RenderIntent renderIntent,
        RenderPullPurpose pullPurpose = RenderPullPurpose.Frame)
    {
        ThrowIfDisposed();
        (renderIntent, pullPurpose) = ValidatePersistentPolicy(renderIntent, pullPurpose);
        _rejectedPolicies.Add((renderIntent, pullPurpose));
    }

    public void Invalidate()
    {
        ThrowIfDisposed();
        if (_cache.Count != 0)
        {
            RenderNodeCacheHelper._logger.LogInformation("Invalidating Cache for {Node}",
                _node.TryGetTarget(out RenderNode? node) ? node : null);
        }

        try
        {
            ClearStoredTargets();
        }
        finally
        {
            _rejectedPolicies.Clear();
        }
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        try
        {
            ClearStoredTargets();
        }
        finally
        {
            _rejectedPolicies.Clear();
            GC.SuppressFinalize(this);
        }
    }

    public RenderTarget UseCache(out Rect bounds)
    {
        ThrowIfDisposed();
        if (_cache.Count == 0)
        {
            throw new Exception("キャッシュはありません");
        }

        (RenderTarget, Rect) c = _cache[0];
        bounds = c.Item2;
        return c.Item1.ShallowCopy();
    }

    public void StoreCache(RenderTarget renderTarget, Rect bounds, float density = 1f)
        => StoreCache(
            renderTarget,
            bounds,
            RenderIntent.Preview,
            density,
            RenderPullPurpose.Frame);

    /// <summary>Stores one persistent frame-cache tile with explicit render-policy provenance.</summary>
    public void StoreCache(
        RenderTarget renderTarget,
        Rect bounds,
        RenderIntent renderIntent,
        float density = 1f,
        RenderPullPurpose pullPurpose = RenderPullPurpose.Frame)
    {
        ThrowIfDisposed();
        (renderIntent, pullPurpose) = ValidatePersistentPolicy(renderIntent, pullPurpose);
        ClearStoredTargets();
        _rejectedPolicies.Remove((renderIntent, pullPurpose));

        _cache.Add((renderTarget.ShallowCopy(), bounds));
        Density = density;
        _cachedRenderIntent = renderIntent;
        _cachedPullPurpose = pullPurpose;
    }

    public IEnumerable<(RenderTarget RenderTarget, Rect Bounds)> UseCache()
    {
        ThrowIfDisposed();
        return _cache.Select(i => (i.Item1.ShallowCopy(), i.Item2));
    }

    public void StoreCache(ReadOnlySpan<(RenderTarget RenderTarget, Rect Bounds)> items, float density = 1f)
        => StoreCache(
            items,
            RenderIntent.Preview,
            density,
            RenderPullPurpose.Frame);

    /// <summary>Stores persistent frame-cache tiles with explicit render-policy provenance.</summary>
    public void StoreCache(
        ReadOnlySpan<(RenderTarget RenderTarget, Rect Bounds)> items,
        RenderIntent renderIntent,
        float density = 1f,
        RenderPullPurpose pullPurpose = RenderPullPurpose.Frame)
    {
        ThrowIfDisposed();
        (renderIntent, pullPurpose) = ValidatePersistentPolicy(renderIntent, pullPurpose);
        var staged = new List<(RenderTarget, Rect)>(items.Length);
        try
        {
            foreach ((RenderTarget renderTarget, Rect bounds) in items)
            {
                staged.Add((renderTarget.ShallowCopy(), bounds));
            }
        }
        catch (Exception ex)
        {
            Exception? failure = ex;
            DisposeTargets(staged, ref failure);
            ExceptionDispatchInfo.Capture(failure!).Throw();
        }

        try
        {
            ClearStoredTargets();
        }
        catch (Exception ex)
        {
            Exception? failure = ex;
            DisposeTargets(staged, ref failure);
            ExceptionDispatchInfo.Capture(failure!).Throw();
        }

        _cache = staged;
        _rejectedPolicies.Remove((renderIntent, pullPurpose));
        Density = density;
        _cachedRenderIntent = renderIntent;
        _cachedPullPurpose = pullPurpose;
    }

    private static void DisposeTargets(List<(RenderTarget, Rect)> items, ref Exception? failure)
    {
        foreach ((RenderTarget renderTarget, _) in items)
        {
            try
            {
                renderTarget.Dispose();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        items.Clear();
    }

    private void ClearStoredTargets()
    {
        (RenderTarget, Rect)[] items = [.. _cache];
        _cache.Clear();
        _cachedRenderIntent = null;
        _cachedPullPurpose = null;
        Exception? failure = null;
        foreach ((RenderTarget, Rect) item in items)
        {
            try
            {
                item.Item1.Dispose();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }

        if (failure != null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static (RenderIntent RenderIntent, RenderPullPurpose PullPurpose) ValidatePersistentPolicy(
        RenderIntent renderIntent,
        RenderPullPurpose pullPurpose)
    {
        renderIntent = RenderPolicyValidation.Validate(renderIntent, nameof(renderIntent));
        pullPurpose = RenderPolicyValidation.Validate(pullPurpose, nameof(pullPurpose));
        if (pullPurpose != RenderPullPurpose.Frame)
        {
            throw new NotSupportedException(
                "Persistent render-node caches are frame-only; auxiliary pulls must execute without retaining or replaying frame-cache state.");
        }

        return (renderIntent, pullPurpose);
    }

    private void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(IsDisposed, this);
}
