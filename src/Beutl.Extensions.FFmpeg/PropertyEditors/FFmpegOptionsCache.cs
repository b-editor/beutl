using System.Diagnostics.CodeAnalysis;

namespace Beutl.Extensions.FFmpeg.PropertyEditors;

/// <summary>
/// One option query's outcome: the items, plus whether they are a worker fallback
/// (<see cref="Degraded"/>) rather than an authoritative answer.
/// </summary>
internal readonly record struct OptionsQueryResult<T>(T[] Items, bool Degraded);

/// <summary>
/// Caches per-codec option arrays (sample rates / pixel formats / audio formats) queried
/// asynchronously from the FFmpeg worker, keyed by an opaque string (codec + output file).
/// Concurrent requests for the same key share a single in-flight query (single-flight).
/// </summary>
/// <remarks>
/// The shared query is not tied to any caller's <see cref="CancellationToken"/>, so abandoning one
/// request never faults the result for others that joined the same query. Degraded results are
/// surfaced to every awaiter but never cached, so the next visit re-queries.
/// </remarks>
internal sealed class FFmpegOptionsCache<T>
{
    private readonly Dictionary<string, T[]> _cache = [];
    private readonly Dictionary<string, Task<OptionsQueryResult<T>>> _inflight = [];
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
    /// Drops every cached entry and abandons single-flight tracking for keys whose factory has not
    /// completed yet. In-flight tasks are not cancelled (their awaiters still observe the result);
    /// only the bookkeeping that dedupes <em>new</em> callers is cleared, so a caller that arrives
    /// after <see cref="Clear"/> may launch a fresh query even while a previous one is still running.
    /// Used to reset a process-shared cache (e.g. after the FFmpeg worker restarts).
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _cache.Clear();
            _inflight.Clear();
        }
    }

    /// <summary>
    /// Returns the cached result for <paramref name="key"/>, or runs <paramref name="factory"/> once
    /// and caches its items unless the result is degraded. Concurrent calls for the same uncached key
    /// share one task and all receive the same <see cref="OptionsQueryResult{T}"/>.
    /// </summary>
    public Task<OptionsQueryResult<T>> GetOrQueryAsync(string key, Func<Task<OptionsQueryResult<T>>> factory)
    {
        lock (_gate)
        {
            // A cached entry is authoritative by construction (degraded results are never stored).
            if (_cache.TryGetValue(key, out var cached))
                return Task.FromResult(new OptionsQueryResult<T>(cached, Degraded: false));
            if (_inflight.TryGetValue(key, out var running))
                return running;

            Task<OptionsQueryResult<T>> task = RunAsync(key, factory);
            _inflight[key] = task;
            return task;
        }
    }

    private async Task<OptionsQueryResult<T>> RunAsync(string key, Func<Task<OptionsQueryResult<T>>> factory)
    {
        try
        {
            // Run the factory on the thread pool so it never executes while the caller holds _gate
            // (GetOrQueryAsync starts this task inside the lock) and never resumes on the UI thread's
            // synchronization context, where the worker's synchronous startup work would stutter the UI.
            OptionsQueryResult<T> result = await Task.Run(factory).ConfigureAwait(false);
            if (!result.Degraded)
            {
                lock (_gate)
                {
                    _cache[key] = result.Items;
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
