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
/// request never faults the result for others that joined it. Degraded results are never cached.
/// Cached entries are bounded by a least-recently-used cap so a process-shared cache cannot grow
/// without limit as keys accumulate over a long editing session.
/// </remarks>
internal sealed class FFmpegOptionsCache<T>
{
    private const int DefaultMaxCachedEntries = 128;

    private readonly Dictionary<string, LinkedListNode<(string Key, T[] Items)>> _cache = [];
    private readonly LinkedList<(string Key, T[] Items)> _recency = new();

    // The token distinguishes a query from one that superseded it after a mid-flight Clear(), so the
    // older query won't cache a stale result or evict the newer registration when it completes.
    private readonly Dictionary<string, (Task<OptionsQueryResult<T>> Task, long Token)> _inflight = [];
    private readonly object _gate = new();
    private readonly int _maxCachedEntries;
    private long _nextToken;

    /// <param name="maxCachedEntries">
    /// Upper bound on retained entries; the least-recently-used one is evicted past the bound and
    /// simply re-queried on its next visit.
    /// </param>
    public FFmpegOptionsCache(int maxCachedEntries = DefaultMaxCachedEntries)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCachedEntries, 1);
        _maxCachedEntries = maxCachedEntries;
    }

    /// <summary>Returns the cached result for <paramref name="key"/> without querying the worker.</summary>
    public bool TryGetCached(string key, [MaybeNullWhen(false)] out T[] value)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out LinkedListNode<(string Key, T[] Items)>? node))
            {
                Touch(node);
                value = node.Value.Items;
                return true;
            }

            value = null;
            return false;
        }
    }

    /// <summary>
    /// Drops every cached entry and the single-flight bookkeeping. In-flight tasks keep running (their
    /// awaiters still get the result), but a caller arriving after <see cref="Clear"/> may launch a
    /// fresh query; that superseded query neither caches its result nor disturbs the newer one.
    /// Used to reset a process-shared cache, e.g. after the FFmpeg worker restarts.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _cache.Clear();
            _recency.Clear();
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
            if (_cache.TryGetValue(key, out LinkedListNode<(string Key, T[] Items)>? node))
            {
                Touch(node);
                return Task.FromResult(new OptionsQueryResult<T>(node.Value.Items, Degraded: false));
            }
            if (_inflight.TryGetValue(key, out var running))
                return running.Task;

            long token = ++_nextToken;
            Task<OptionsQueryResult<T>> task = RunAsync(key, token, factory);
            _inflight[key] = (task, token);
            return task;
        }
    }

    private async Task<OptionsQueryResult<T>> RunAsync(
        string key, long token, Func<Task<OptionsQueryResult<T>>> factory)
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
                    // Skip the write if a Clear() or later query superseded us; caching now would
                    // resurrect a stale result.
                    if (IsCurrent(key, token))
                        Store(key, result.Items);
                }
            }

            return result;
        }
        finally
        {
            lock (_gate)
            {
                // Remove only our own registration; a newer query may now own this key.
                if (IsCurrent(key, token))
                    _inflight.Remove(key);
            }
        }
    }

    private bool IsCurrent(string key, long token)
        => _inflight.TryGetValue(key, out var entry) && entry.Token == token;

    private void Store(string key, T[] items)
    {
        if (_cache.TryGetValue(key, out LinkedListNode<(string Key, T[] Items)>? existing))
        {
            existing.Value = (key, items);
            Touch(existing);
            return;
        }

        _cache[key] = _recency.AddFirst((key, items));
        if (_cache.Count > _maxCachedEntries && _recency.Last is { } lru)
        {
            _recency.RemoveLast();
            _cache.Remove(lru.Value.Key);
        }
    }

    private void Touch(LinkedListNode<(string Key, T[] Items)> node)
    {
        _recency.Remove(node);
        _recency.AddFirst(node);
    }
}
