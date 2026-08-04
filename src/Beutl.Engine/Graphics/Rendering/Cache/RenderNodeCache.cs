using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Beutl.Media;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Rendering.Cache;

/// <summary>Stores reusable render outputs for one render node.</summary>
/// <remarks>
/// Cache access is serialized by the owning render lifetime and render thread.
/// This type does not provide independent synchronization and must not be accessed concurrently.
/// </remarks>
public sealed class RenderNodeCache(RenderNode node) : IDisposable
{
    private readonly WeakReference<RenderNode> _node = new(node);
    private CacheStorage _storage = CacheStorage.Empty;

    public const int Count = 3;

    private int _count;

    ~RenderNodeCache()
    {
        Dispose(disposing: false);
    }

    public bool IsCached => _storage.Identity is not null || _storage.Values.Length != 0;

    public int CacheCount => _storage.Values.Length;

    internal float IdentityDensity => _storage.IdentityDensity;

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
        try
        {
            Dispose(disposing: true);
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }

    private void Dispose(bool disposing)
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        CacheStorage storage = DetachStorage();
        if (disposing)
        {
            DisposeStorage(storage);
            return;
        }

        try
        {
            DisposeStorage(storage);
        }
        catch
        {
            // Finalizers must never let cleanup failures terminate the process.
        }
    }

    internal RenderTarget UseCache(out Rect bounds)
    {
        if (_storage.Values.Length == 0)
        {
            throw new InvalidOperationException("No cached render target is available.");
        }

        RenderNodeCachedValue value = _storage.Values[0];
        bounds = value.Bounds;
        return value.Target.ShallowCopy();
    }

    internal IEnumerable<(RenderTarget RenderTarget, Rect Bounds)> UseCache()
    {
        return _storage.Values
            .Select(static value => (value.Target.ShallowCopy(), value.Bounds))
            .ToArray();
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
        float IdentityDensity)
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
        PixelRect deviceBounds,
        Vector deviceGridOffset = default)
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
        PixelRect semanticDeviceBounds = PixelRect.FromRect(
            bounds.Translate(deviceGridOffset),
            effectiveScale.Value);
        if (deviceBounds.X > semanticDeviceBounds.X
            || deviceBounds.Y > semanticDeviceBounds.Y
            || deviceBounds.Right < semanticDeviceBounds.Right
            || deviceBounds.Bottom < semanticDeviceBounds.Bottom)
        {
            throw new ArgumentException(
                "Cached value device bounds must contain its semantic bounds.",
                nameof(deviceBounds));
        }

        Target = target;
        Bounds = bounds;
        CompleteBounds = bounds;
        EffectiveScale = effectiveScale;
        DeviceBounds = deviceBounds;
        DeviceGridOffset = deviceGridOffset;
    }

    public RenderTarget Target { get; }

    public Rect Bounds { get; }

    public Rect CompleteBounds { get; init; }

    public EffectiveScale EffectiveScale { get; }

    public PixelRect DeviceBounds { get; }

    public Vector DeviceGridOffset { get; }

    public Rect RasterBounds
        => DeviceBounds
            .ToRect(EffectiveScale.Value)
            .Translate(-DeviceGridOffset);

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
