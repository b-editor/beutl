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
internal sealed class RenderNodeCache(RenderNode node) : IDisposable
{
    private readonly WeakReference<RenderNode> _node = new(node);
    private CacheStorage _storage = CacheStorage.Empty;

    internal const int StableRequestCount = 3;

    private int _successfulStableRequests;

    ~RenderNodeCache()
    {
        Dispose(disposing: false);
    }

    internal bool IsCached => _storage.Identity is not null || _storage.Values.Length != 0;

    internal int CacheCount => _storage.Values.Length;

    internal float IdentityDensity => _storage.IdentityDensity;

    internal Type? NodeType => _node.TryGetTarget(out RenderNode? node) ? node.GetType() : null;

    internal bool IsDisposed { get; private set; }

    internal int SuccessfulStableRequestCount => _successfulStableRequests;

    internal bool CanCapture => _successfulStableRequests >= StableRequestCount;

    /// <summary>
    /// The dependency closure's change-version stamp at the request that published this cache, or 0 when
    /// unstamped. Root-independent, so a change another root already consumed from
    /// <see cref="RenderNode.HasChanges"/> still shows up here.
    /// </summary>
    internal long DependencySignature { get; set; }

    internal void RecordSuccessfulStableRequest()
    {
        if (!IsDisposed && _successfulStableRequests < StableRequestCount)
            _successfulStableRequests++;
    }

    internal void Reset()
    {
        if (IsDisposed)
            return;

        _successfulStableRequests = 0;
        DependencySignature = 0;
        InvalidateStorage();
    }

    private void InvalidateStorage()
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
        DependencySignature = 0;
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
