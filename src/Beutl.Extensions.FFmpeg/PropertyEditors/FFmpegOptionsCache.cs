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
/// </remarks>
internal sealed class FFmpegOptionsCache<T>
{
    private readonly Dictionary<string, T[]> _cache = [];
    private readonly Dictionary<string, Task<T[]>> _inflight = [];
    private readonly object _gate = new();

    /// <summary>Returns the cached result for <paramref name="key"/> without querying the worker.</summary>
    public bool TryGetCached(string key, out T[] value)
    {
        lock (_gate)
        {
            return _cache.TryGetValue(key, out value!);
        }
    }

    /// <summary>
    /// Returns the cached result for <paramref name="key"/>, or runs <paramref name="factory"/>
    /// once and caches its result. Repeated calls for an already-cached key never invoke
    /// <paramref name="factory"/> again; concurrent calls for the same uncached key share one task.
    /// </summary>
    public Task<T[]> GetOrQueryAsync(string key, Func<Task<T[]>> factory)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var cached))
                return Task.FromResult(cached);
            if (_inflight.TryGetValue(key, out var running))
                return running;

            Task<T[]> task = RunAsync(key, factory);
            _inflight[key] = task;
            return task;
        }
    }

    private async Task<T[]> RunAsync(string key, Func<Task<T[]>> factory)
    {
        // Yield before invoking the factory so it never runs while the caller holds _gate
        // (GetOrQueryAsync starts this task inside the lock).
        await Task.Yield();
        try
        {
            T[] result = await factory().ConfigureAwait(false);
            lock (_gate)
            {
                _cache[key] = result;
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
