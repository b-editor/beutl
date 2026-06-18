using System.Diagnostics.CodeAnalysis;

namespace Beutl.Extensions.FFmpeg.PropertyEditors;

/// <summary>
/// Caches per-codec option arrays (sample rates / pixel formats / audio formats) queried
/// asynchronously from the FFmpeg worker, keyed by an opaque string (codec + output file).
/// Successful results are cached; concurrent requests for the same key share a single
/// in-flight query (single-flight). Failed queries are not cached so the next request retries.
/// </summary>
/// <remarks>
/// The property editors used to block the UI thread with
/// <c>EnsureStartedAsync().GetAwaiter().GetResult()</c>; this type keeps the data flow async and
/// avoids re-querying the worker when a codec is revisited. The shared query is intentionally not
/// tied to any single caller's <see cref="CancellationToken"/>: callers decide whether to apply a
/// (possibly stale) result themselves, so abandoning one request never faults the result for the
/// others that joined the same in-flight query.
/// <para>
/// An empty successful result can optionally be left uncached (see the <c>cacheEmptyResults</c>
/// parameter of <see cref="GetOrQueryAsync"/>). The worker returns an empty payload both for a
/// genuine "no constrained options" answer and as a soft fallback when its own codec lookup throws;
/// the two are indistinguishable across the IPC boundary, so a caller that cannot tell them apart
/// can decline to cache empties and let the next visit re-query instead of pinning a possibly
/// transient empty for the editor's lifetime.
/// </para>
/// </remarks>
internal sealed class FFmpegOptionsCache<T>
{
    private readonly Dictionary<string, T[]> _cache = [];
    private readonly Dictionary<string, Task<T[]>> _inflight = [];
    private readonly object _gate = new();

    /// <summary>Returns the cached result for <paramref name="key"/> without querying the worker.</summary>
    public bool TryGetCached(string key, [MaybeNullWhen(false)] out T[] value)
    {
        lock (_gate)
        {
            return _cache.TryGetValue(key, out value);
        }
    }

    /// <summary>
    /// Returns the cached result for <paramref name="key"/>, or runs <paramref name="factory"/>
    /// once and caches its result. Repeated calls for an already-cached key never invoke
    /// <paramref name="factory"/> again; concurrent calls for the same uncached key share one task.
    /// When <paramref name="cacheEmptyResults"/> is <see langword="false"/>, an empty result is
    /// still returned to callers but is not stored, so the next request re-queries.
    /// </summary>
    public Task<T[]> GetOrQueryAsync(string key, Func<Task<T[]>> factory, bool cacheEmptyResults = true)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached))
                return Task.FromResult(cached);
            if (_inflight.TryGetValue(key, out var running))
                return running;

            Task<T[]> task = RunAsync(key, factory, cacheEmptyResults);
            _inflight[key] = task;
            return task;
        }
    }

    private async Task<T[]> RunAsync(string key, Func<Task<T[]>> factory, bool cacheEmptyResults)
    {
        try
        {
            // Run the factory on the thread pool so it never executes while the caller holds _gate
            // (GetOrQueryAsync starts this task inside the lock) and never resumes on the UI thread's
            // synchronization context, where the worker's synchronous startup work would stutter the UI.
            T[] result = await Task.Run(factory).ConfigureAwait(false);
            if (cacheEmptyResults || result.Length > 0)
            {
                lock (_gate)
                {
                    _cache[key] = result;
                }
            }

            return result;
        }
        finally
        {
            lock (_gate)
            {
                _inflight.Remove(key);
            }
        }
    }
}
