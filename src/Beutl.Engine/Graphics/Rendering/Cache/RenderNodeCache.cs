using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Beutl.Media;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering.Cache;

public sealed class RenderNodeCache(RenderNode node) : IDisposable
{
    private readonly WeakReference<RenderNode> _node = new(node);
    private CacheStorage _storage = CacheStorage.Empty;

    public const int Count = 3;

    private int _count;

    ~RenderNodeCache()
    {
        if (!IsDisposed)
            Dispose();
    }

    public bool IsCached => _storage.Identity is not null || _storage.Values.Length != 0;

    public int CacheCount => _storage.Values.Length;

    /// <summary>
    /// The pixel density the cached tiles were rasterized at. Replay re-tags tiles at this density.
    /// </summary>
    public float Density => _storage.Density;

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
            Invalidate();
        }
    }

    public bool CanCache()
    {
        return _count >= Count;
    }

    public void Invalidate()
    {
        CacheStorage previous = DetachStorage();
        if (previous.Identity is not null || previous.Values.Length != 0)
        {
            RenderNodeCacheHelper._logger.LogInformation("Invalidating Cache for {Node}",
                _node.TryGetTarget(out RenderNode? node) ? node : null);
        }

        DisposeStorage(previous);
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            IsDisposed = true;
            GC.SuppressFinalize(this);
            DisposeStorage(DetachStorage());
        }
    }

    public RenderTarget UseCache(out Rect bounds)
    {
        if (_storage.Values.Length == 0)
        {
            throw new Exception("キャッシュはありません");
        }

        RenderNodeCachedValue value = _storage.Values[0];
        bounds = value.Bounds;
        return value.Target.ShallowCopy();
    }

    public void StoreCache(RenderTarget renderTarget, Rect bounds, float density = 1f)
    {
        ArgumentNullException.ThrowIfNull(renderTarget);
        StoreCache([(renderTarget, bounds)], density);
    }

    public IEnumerable<(RenderTarget RenderTarget, Rect Bounds)> UseCache()
    {
        return _storage.Values
            .Select(static value => (value.Target.ShallowCopy(), value.Bounds))
            .ToArray();
    }

    public void StoreCache(ReadOnlySpan<(RenderTarget RenderTarget, Rect Bounds)> items, float density = 1f)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!float.IsFinite(density) || density <= 0)
            throw new ArgumentOutOfRangeException(nameof(density), density, "Cache density must be finite and positive.");

        var values = new RenderNodeCachedValue[items.Length];
        int initialized = 0;
        try
        {
            for (; initialized < items.Length; initialized++)
            {
                (RenderTarget renderTarget, Rect bounds) = items[initialized];
                ArgumentNullException.ThrowIfNull(renderTarget);
                if (!RenderRectValidation.IsFiniteNonNegative(bounds))
                    throw new ArgumentException("Cache bounds must be finite and non-negative.", nameof(items));
                values[initialized] = new RenderNodeCachedValue(
                    renderTarget.ShallowCopy(),
                    bounds,
                    EffectiveScale.At(density));
            }

            ReplaceStorage(new CacheStorage(Identity: null, values, density));
        }
        catch
        {
            for (int index = initialized - 1; index >= 0; index--)
                values[index].Target.Dispose();
            throw;
        }
    }

    internal bool TryGetCachedOutput(
        RenderOutputCacheIdentity identity,
        out RenderNodeCachedOutput? output)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (IsDisposed || _storage.Identity is null || !_storage.Identity.Equals(identity))
        {
            output = null;
            return false;
        }

        output = new RenderNodeCachedOutput(_storage.Values);
        return true;
    }

    internal static IReadOnlyList<Exception> PublishAtomically(
        IReadOnlyList<RenderNodeCachePublication> publications)
    {
        ArgumentNullException.ThrowIfNull(publications);
        if (publications.Count == 0)
            return [];

        var seen = new HashSet<RenderNodeCache>(ReferenceEqualityComparer.Instance);
        var prepared = new (RenderNodeCache Cache, CacheStorage Storage)[publications.Count];
        for (int index = 0; index < publications.Count; index++)
        {
            RenderNodeCachePublication publication = publications[index]
                ?? throw new ArgumentException("A cache-publication batch cannot contain null entries.", nameof(publications));
            RenderNodeCache cache = publication.Cache;
            ObjectDisposedException.ThrowIf(cache.IsDisposed, cache);
            if (!seen.Add(cache))
            {
                throw new InvalidOperationException(
                    "One atomic cache-publication batch cannot replace the same node cache twice.");
            }

            RenderNodeCachedValue[] values = publication.Values.ToArray();
            foreach (RenderNodeCachedValue value in values)
            {
                ArgumentNullException.ThrowIfNull(value);
                ObjectDisposedException.ThrowIf(value.Target.IsDisposed, value.Target);
                if (!RenderRectValidation.IsFiniteNonNegative(value.Bounds)
                    || value.EffectiveScale.IsUnbounded
                    || value.DeviceBounds.Size != new PixelSize(value.Target.Width, value.Target.Height))
                {
                    throw new InvalidOperationException(
                        "A cache publication requires finite bounds, a concrete density, and matching device bounds.");
                }
            }

            prepared[index] = (
                cache,
                new CacheStorage(publication.Identity, values, publication.Identity.Density));
        }

        var previous = new CacheStorage[prepared.Length];
        for (int index = 0; index < prepared.Length; index++)
            previous[index] = prepared[index].Cache._storage;

        // Validation and allocation are complete. These reference assignments are the
        // publication commit point and cannot invoke user cleanup code or partially fail.
        foreach ((RenderNodeCache cache, CacheStorage storage) in prepared)
            cache._storage = storage;

        List<Exception>? failures = null;
        for (int index = previous.Length - 1; index >= 0; index--)
        {
            try
            {
                DisposeStorage(previous[index]);
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        return failures ?? [];
    }

    private void ReplaceStorage(CacheStorage replacement)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        CacheStorage previous = DetachStorage();
        DisposeStorage(previous);
        _storage = replacement;
    }

    private CacheStorage DetachStorage()
    {
        CacheStorage result = _storage;
        _storage = CacheStorage.Empty;
        return result;
    }

    private static void DisposeStorage(CacheStorage storage)
    {
        List<Exception>? failures = null;
        for (int index = storage.Values.Length - 1; index >= 0; index--)
        {
            try
            {
                storage.Values[index].Target.Dispose();
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is null)
            return;
        if (failures.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        throw new AggregateException("One or more render-cache targets failed to dispose.", failures);
    }

    private sealed record CacheStorage(
        RenderOutputCacheIdentity? Identity,
        RenderNodeCachedValue[] Values,
        float Density)
    {
        public static CacheStorage Empty { get; } = new(null, [], 1);
    }
}

internal sealed record RenderNodeCachedValue
{
    public RenderNodeCachedValue(
        RenderTarget target,
        Rect bounds,
        EffectiveScale effectiveScale)
        : this(
            target,
            bounds,
            effectiveScale,
            CreateDeviceBounds(target, bounds, effectiveScale))
    {
    }

    public RenderNodeCachedValue(
        RenderTarget target,
        Rect bounds,
        EffectiveScale effectiveScale,
        PixelRect deviceBounds)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!RenderRectValidation.IsFiniteNonNegative(bounds))
            throw new ArgumentException("Cached value bounds must be finite and non-negative.", nameof(bounds));
        if (effectiveScale.IsUnbounded)
            throw new ArgumentException("A cached value requires a concrete density.", nameof(effectiveScale));
        if (deviceBounds.Width < 0 || deviceBounds.Height < 0)
            throw new ArgumentException("Cached value device bounds cannot have negative dimensions.", nameof(deviceBounds));
        if (deviceBounds.Size != new PixelSize(target.Width, target.Height))
        {
            throw new ArgumentException(
                "Cached value device bounds must match the backing target size.",
                nameof(deviceBounds));
        }

        Target = target;
        Bounds = bounds;
        CompleteBounds = bounds;
        EffectiveScale = effectiveScale;
        DeviceBounds = deviceBounds;
    }

    public RenderTarget Target { get; }

    public Rect Bounds { get; }

    public Rect CompleteBounds { get; init; }

    public EffectiveScale EffectiveScale { get; }

    public PixelRect DeviceBounds { get; }

    public Rect RasterBounds => DeviceBounds.ToRect(EffectiveScale.Value);

    private static PixelRect CreateDeviceBounds(
        RenderTarget target,
        Rect bounds,
        EffectiveScale effectiveScale)
    {
        ArgumentNullException.ThrowIfNull(target);
        PixelRect canonical = PixelRect.FromRect(bounds, effectiveScale.Value);
        return new PixelRect(canonical.Position, new PixelSize(target.Width, target.Height));
    }
}

internal sealed class RenderNodeCachedOutput
{
    public RenderNodeCachedOutput(IReadOnlyList<RenderNodeCachedValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        Values = values;
    }

    public IReadOnlyList<RenderNodeCachedValue> Values { get; }
}

internal sealed record RenderNodeCachePublication(
    RenderNodeCache Cache,
    RenderOutputCacheIdentity Identity,
    IReadOnlyList<RenderNodeCachedValue> Values);
