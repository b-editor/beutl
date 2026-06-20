using Beutl.Extensions.FFmpeg.PropertyEditors;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public class FFmpegOptionsCacheTests
{
    private static Task<OptionsQueryResult<int>> Ok(params int[] items)
        => Task.FromResult(new OptionsQueryResult<int>(items, Degraded: false));

    private static Task<OptionsQueryResult<int>> Degraded(params int[] items)
        => Task.FromResult(new OptionsQueryResult<int>(items, Degraded: true));

    [Test]
    public async Task GetOrQueryAsync_FirstCall_InvokesFactory()
    {
        var cache = new FFmpegOptionsCache<int>();
        int calls = 0;

        OptionsQueryResult<int> result = await cache.GetOrQueryAsync(
            "aac", () => { calls++; return Ok(44100, 48000); });

        Assert.That(calls, Is.EqualTo(1));
        Assert.That(result.Items, Is.EqualTo(new[] { 44100, 48000 }));
        Assert.That(result.Degraded, Is.False);
    }

    [Test]
    public async Task GetOrQueryAsync_SecondCallSameKey_ServesFromCacheWithoutReinvoking()
    {
        var cache = new FFmpegOptionsCache<int>();
        int calls = 0;

        await cache.GetOrQueryAsync("aac", () => { calls++; return Ok(48000); });
        OptionsQueryResult<int> second = await cache.GetOrQueryAsync(
            "aac", () => { calls++; return Ok(-1); });

        // The cached value is returned and the factory is not invoked a second time
        // (this is what stops the editor re-blocking / re-querying the worker on re-selection).
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(second.Items, Is.EqualTo(new[] { 48000 }));
        // A cached entry is authoritative by construction.
        Assert.That(second.Degraded, Is.False);
    }

    [Test]
    public async Task GetOrQueryAsync_DifferentKey_InvokesFactoryAgain()
    {
        var cache = new FFmpegOptionsCache<int>();
        int calls = 0;

        await cache.GetOrQueryAsync("aac", () => { calls++; return Ok(48000); });
        OptionsQueryResult<int> other = await cache.GetOrQueryAsync(
            "mp3", () => { calls++; return Ok(44100); });

        Assert.That(calls, Is.EqualTo(2));
        Assert.That(other.Items, Is.EqualTo(new[] { 44100 }));
    }

    [Test]
    public async Task GetOrQueryAsync_FactoryThrows_NotCached_AndRetriesOnNextCall()
    {
        var cache = new FFmpegOptionsCache<int>();
        int calls = 0;

        Assert.ThrowsAsync<InvalidOperationException>(
            () => cache.GetOrQueryAsync(
                "aac",
                () => { calls++; return Task.FromException<OptionsQueryResult<int>>(new InvalidOperationException()); }));

        // A failed query must not be cached, so the next request runs the factory again and can succeed.
        OptionsQueryResult<int> retry = await cache.GetOrQueryAsync(
            "aac", () => { calls++; return Ok(48000); });

        Assert.That(retry.Items, Is.EqualTo(new[] { 48000 }));
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public async Task TryGetCached_ReflectsCacheState()
    {
        var cache = new FFmpegOptionsCache<int>();

        Assert.That(cache.TryGetCached("aac", out _), Is.False);

        await cache.GetOrQueryAsync("aac", () => Ok(48000));

        Assert.That(cache.TryGetCached("aac", out int[]? cached), Is.True);
        Assert.That(cached, Is.EqualTo(new[] { 48000 }));
    }

    [Test]
    public async Task GetOrQueryAsync_DegradedResult_NotCached_AndRetries()
    {
        var cache = new FFmpegOptionsCache<int>();
        int calls = 0;

        // A degraded (worker-fallback) result is surfaced to the caller but must not be pinned in the
        // cache, so the next request re-queries instead of serving the stale fallback forever.
        OptionsQueryResult<int> first = await cache.GetOrQueryAsync(
            "aac", () => { calls++; return Degraded(44100, 48000); });

        Assert.That(first.Items, Is.EqualTo(new[] { 44100, 48000 }));
        Assert.That(first.Degraded, Is.True);
        Assert.That(cache.TryGetCached("aac", out _), Is.False);

        OptionsQueryResult<int> second = await cache.GetOrQueryAsync(
            "aac", () => { calls++; return Ok(48000); });

        Assert.That(calls, Is.EqualTo(2));
        Assert.That(second.Items, Is.EqualTo(new[] { 48000 }));
    }

    [Test]
    public async Task GetOrQueryAsync_AuthoritativeResult_Cached()
    {
        var cache = new FFmpegOptionsCache<int>();
        int calls = 0;

        await cache.GetOrQueryAsync("aac", () => { calls++; return Ok(48000); });
        OptionsQueryResult<int> second = await cache.GetOrQueryAsync(
            "aac", () => { calls++; return Ok(-1); });

        // A non-degraded result is cached and served like any cached value.
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(second.Items, Is.EqualTo(new[] { 48000 }));
    }

    [Test]
    public async Task GetOrQueryAsync_AuthoritativeEmptyResult_Cached()
    {
        var cache = new FFmpegOptionsCache<int>();
        int calls = 0;

        await cache.GetOrQueryAsync("aac", () => { calls++; return Ok(); });

        // An authoritative empty result ("no constrained options") is genuine, so it is cached.
        Assert.That(cache.TryGetCached("aac", out _), Is.True);
        OptionsQueryResult<int> second = await cache.GetOrQueryAsync(
            "aac", () => { calls++; return Ok(-1); });

        Assert.That(calls, Is.EqualTo(1));
        Assert.That(second.Items, Is.Empty);
    }

    [Test]
    public async Task GetOrQueryAsync_ConcurrentSameKey_SharesSingleInFlightQuery()
    {
        var cache = new FFmpegOptionsCache<int>();
        int calls = 0;
        var gate = new TaskCompletionSource<OptionsQueryResult<int>>();

        Task<OptionsQueryResult<int>> first = cache.GetOrQueryAsync("aac", () => { calls++; return gate.Task; });
        Task<OptionsQueryResult<int>> second = cache.GetOrQueryAsync("aac", () => { calls++; return gate.Task; });

        gate.SetResult(new OptionsQueryResult<int>([48000], Degraded: false));
        OptionsQueryResult<int>[] results = await Task.WhenAll(first, second);

        // Single-flight: the second caller joins the in-flight query rather than launching its own.
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(results[0].Items, Is.EqualTo(new[] { 48000 }));
        Assert.That(results[1].Items, Is.EqualTo(new[] { 48000 }));
    }

    [Test]
    public async Task GetOrQueryAsync_InFlightJoiner_AlsoSeesDegradedFlag()
    {
        // A joiner shares the in-flight task, so it must observe the same Degraded verdict — otherwise a
        // joiner could apply a degraded (empty) result as authoritative and clobber the user's selection.
        var cache = new FFmpegOptionsCache<int>();
        var gate = new TaskCompletionSource<OptionsQueryResult<int>>();

        Task<OptionsQueryResult<int>> first = cache.GetOrQueryAsync("aac", () => gate.Task);
        Task<OptionsQueryResult<int>> joiner = cache.GetOrQueryAsync("aac", () => Ok(-1));

        gate.SetResult(new OptionsQueryResult<int>([], Degraded: true));
        OptionsQueryResult<int>[] results = await Task.WhenAll(first, joiner);

        Assert.That(results[0].Degraded, Is.True);
        Assert.That(results[1].Degraded, Is.True);
        // And the shared degraded result is not pinned in the cache.
        Assert.That(cache.TryGetCached("aac", out _), Is.False);
    }

    [Test]
    public async Task GetOrQueryAsync_JoinerStillGetsResult_WhenAnEarlierCallerAbandonsItsAwait()
    {
        // Regression guard: the shared in-flight task must not be tied to any single caller, so a
        // caller that joins after another has stopped awaiting still receives the completed result.
        var cache = new FFmpegOptionsCache<int>();
        var gate = new TaskCompletionSource<OptionsQueryResult<int>>();

        Task<OptionsQueryResult<int>> abandoned = cache.GetOrQueryAsync("aac", () => gate.Task);
        Task<OptionsQueryResult<int>> joiner = cache.GetOrQueryAsync("aac", () => Ok(-1));
        // Simulate the first caller losing interest (e.g. its codec selection was superseded).
        _ = abandoned;

        gate.SetResult(new OptionsQueryResult<int>([48000], Degraded: false));

        Assert.That((await joiner).Items, Is.EqualTo(new[] { 48000 }));
    }

    [Test]
    public void Clear_OnEmptyCache_IsNoOp()
    {
        var cache = new FFmpegOptionsCache<int>();

        cache.Clear();

        Assert.That(cache.TryGetCached("aac", out _), Is.False);
    }

    [Test]
    public async Task Clear_AfterPopulate_RemovesEntry()
    {
        var cache = new FFmpegOptionsCache<int>();
        await cache.GetOrQueryAsync("aac", () => Ok(48000));
        Assert.That(cache.TryGetCached("aac", out _), Is.True);

        cache.Clear();

        Assert.That(cache.TryGetCached("aac", out _), Is.False);
    }

    [Test]
    public async Task Clear_ForcesNextCallToReQuery()
    {
        var cache = new FFmpegOptionsCache<int>();
        int calls = 0;
        await cache.GetOrQueryAsync("aac", () => { calls++; return Ok(48000); });

        cache.Clear();

        OptionsQueryResult<int> again = await cache.GetOrQueryAsync(
            "aac", () => { calls++; return Ok(44100); });

        // After Clear the cached entry is gone, so the factory runs again.
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(again.Items, Is.EqualTo(new[] { 44100 }));
        Assert.That(cache.TryGetCached("aac", out int[]? cached), Is.True);
        Assert.That(cached, Is.EqualTo(new[] { 44100 }));
    }

    [Test]
    public async Task Clear_WhileQueryInFlight_StaleResultIsNotCached()
    {
        // A query that started before Clear() must not write its (now stale) result into the cache
        // after the reset — otherwise a worker-restart reset is silently undone by the old answer.
        var cache = new FFmpegOptionsCache<int>();
        var stale = new TaskCompletionSource<OptionsQueryResult<int>>();
        var fresh = new TaskCompletionSource<OptionsQueryResult<int>>();

        Task<OptionsQueryResult<int>> before = cache.GetOrQueryAsync("aac", () => stale.Task);
        cache.Clear();
        Task<OptionsQueryResult<int>> after = cache.GetOrQueryAsync("aac", () => fresh.Task);

        // The pre-Clear query completes first, but its result belongs to the discarded generation.
        stale.SetResult(new OptionsQueryResult<int>([11025], Degraded: false));
        await before;
        Assert.That(cache.TryGetCached("aac", out _), Is.False);

        // The post-Clear query is the one that populates the cache.
        fresh.SetResult(new OptionsQueryResult<int>([48000], Degraded: false));
        await after;
        Assert.That(cache.TryGetCached("aac", out int[]? cached), Is.True);
        Assert.That(cached, Is.EqualTo(new[] { 48000 }));
    }

    [Test]
    public async Task Clear_WhileQueryInFlight_StaleCompletionKeepsNewerInFlightEntry()
    {
        // The pre-Clear query's cleanup removes its in-flight bookkeeping by key; it must not evict the
        // newer query that took over the key after Clear(), or single-flight breaks and a later caller
        // would launch a duplicate worker query instead of joining the running one.
        var cache = new FFmpegOptionsCache<int>();
        var stale = new TaskCompletionSource<OptionsQueryResult<int>>();
        var fresh = new TaskCompletionSource<OptionsQueryResult<int>>();

        Task<OptionsQueryResult<int>> before = cache.GetOrQueryAsync("aac", () => stale.Task);
        cache.Clear();
        Task<OptionsQueryResult<int>> after = cache.GetOrQueryAsync("aac", () => fresh.Task);

        // The pre-Clear query completes (degraded, so it never caches) and runs its cleanup.
        stale.SetResult(new OptionsQueryResult<int>([], Degraded: true));
        await before;

        // The post-Clear query is still in flight, so a new caller must join it without re-querying.
        // The joiner returns a distinct value, so a single-flight regression (joining failing and the
        // joiner running its own query) is caught by both the unused-factory count and the result below
        // rather than being masked by the two queries happening to share the same gate.
        int extraCalls = 0;
        Task<OptionsQueryResult<int>> joiner = cache.GetOrQueryAsync(
            "aac", () => { extraCalls++; return Ok(-1); });

        fresh.SetResult(new OptionsQueryResult<int>([48000], Degraded: false));
        OptionsQueryResult<int>[] results = await Task.WhenAll(after, joiner);
        Assert.That(extraCalls, Is.EqualTo(0));
        Assert.That(results[0].Items, Is.EqualTo(new[] { 48000 }));
        Assert.That(results[1].Items, Is.EqualTo(new[] { 48000 }));
    }

    [Test]
    public async Task Cache_EvictsLeastRecentlyUsed_WhenOverCapacity()
    {
        var cache = new FFmpegOptionsCache<int>(maxCachedEntries: 2);
        await cache.GetOrQueryAsync("a", () => Ok(1));
        await cache.GetOrQueryAsync("b", () => Ok(2));

        // Touch "a" so "b" is now the least-recently-used entry.
        Assert.That(cache.TryGetCached("a", out _), Is.True);

        // Inserting a third key exceeds the cap of 2 and evicts the LRU entry ("b").
        await cache.GetOrQueryAsync("c", () => Ok(3));

        Assert.That(cache.TryGetCached("a", out _), Is.True);
        Assert.That(cache.TryGetCached("c", out _), Is.True);
        Assert.That(cache.TryGetCached("b", out _), Is.False);
    }
}
