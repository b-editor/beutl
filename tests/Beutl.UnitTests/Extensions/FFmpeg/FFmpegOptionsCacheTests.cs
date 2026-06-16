using Beutl.Extensions.FFmpeg.PropertyEditors;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public class FFmpegOptionsCacheTests
{
    [Test]
    public async Task GetOrQueryAsync_FirstCall_InvokesFactory()
    {
        var cache = new FFmpegOptionsCache<int>();
        int calls = 0;

        int[] result = await cache.GetOrQueryAsync(
            "aac", () => { calls++; return Task.FromResult(new[] { 44100, 48000 }); });

        Assert.That(calls, Is.EqualTo(1));
        Assert.That(result, Is.EqualTo(new[] { 44100, 48000 }));
    }

    [Test]
    public async Task GetOrQueryAsync_SecondCallSameKey_ServesFromCacheWithoutReinvoking()
    {
        var cache = new FFmpegOptionsCache<int>();
        int calls = 0;

        await cache.GetOrQueryAsync("aac", () => { calls++; return Task.FromResult(new[] { 48000 }); });
        int[] second = await cache.GetOrQueryAsync(
            "aac", () => { calls++; return Task.FromResult(new[] { -1 }); });

        // The cached value is returned and the factory is not invoked a second time
        // (this is what stops the editor re-blocking / re-querying the worker on re-selection).
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(second, Is.EqualTo(new[] { 48000 }));
    }

    [Test]
    public async Task GetOrQueryAsync_DifferentKey_InvokesFactoryAgain()
    {
        var cache = new FFmpegOptionsCache<int>();
        int calls = 0;

        await cache.GetOrQueryAsync("aac", () => { calls++; return Task.FromResult(new[] { 48000 }); });
        int[] other = await cache.GetOrQueryAsync(
            "mp3", () => { calls++; return Task.FromResult(new[] { 44100 }); });

        Assert.That(calls, Is.EqualTo(2));
        Assert.That(other, Is.EqualTo(new[] { 44100 }));
    }

    [Test]
    public async Task GetOrQueryAsync_FactoryThrows_NotCached_AndRetriesOnNextCall()
    {
        var cache = new FFmpegOptionsCache<int>();
        int calls = 0;

        Assert.ThrowsAsync<InvalidOperationException>(
            () => cache.GetOrQueryAsync(
                "aac", () => { calls++; return Task.FromException<int[]>(new InvalidOperationException()); }));

        // A failed query must not be cached, so the next request runs the factory again and can succeed.
        int[] retry = await cache.GetOrQueryAsync(
            "aac", () => { calls++; return Task.FromResult(new[] { 48000 }); });

        Assert.That(retry, Is.EqualTo(new[] { 48000 }));
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public async Task TryGetCached_ReflectsCacheState()
    {
        var cache = new FFmpegOptionsCache<int>();

        Assert.That(cache.TryGetCached("aac", out _), Is.False);

        await cache.GetOrQueryAsync("aac", () => Task.FromResult(new[] { 48000 }));

        Assert.That(cache.TryGetCached("aac", out int[]? cached), Is.True);
        Assert.That(cached, Is.EqualTo(new[] { 48000 }));
    }

    [Test]
    public async Task GetOrQueryAsync_ConcurrentSameKey_SharesSingleInFlightQuery()
    {
        var cache = new FFmpegOptionsCache<int>();
        int calls = 0;
        var gate = new TaskCompletionSource<int[]>();

        Task<int[]> first = cache.GetOrQueryAsync("aac", () => { calls++; return gate.Task; });
        Task<int[]> second = cache.GetOrQueryAsync("aac", () => { calls++; return gate.Task; });

        gate.SetResult([48000]);
        int[][] results = await Task.WhenAll(first, second);

        // Single-flight: the second caller joins the in-flight query rather than launching its own.
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(results[0], Is.EqualTo(new[] { 48000 }));
        Assert.That(results[1], Is.EqualTo(new[] { 48000 }));
    }

    [Test]
    public async Task GetOrQueryAsync_JoinerStillGetsResult_WhenAnEarlierCallerAbandonsItsAwait()
    {
        // Regression guard: the shared in-flight task must not be tied to any single caller, so a
        // caller that joins after another has stopped awaiting still receives the completed result.
        var cache = new FFmpegOptionsCache<int>();
        var gate = new TaskCompletionSource<int[]>();

        Task<int[]> abandoned = cache.GetOrQueryAsync("aac", () => gate.Task);
        Task<int[]> joiner = cache.GetOrQueryAsync("aac", () => Task.FromResult(new[] { -1 }));
        // Simulate the first caller losing interest (e.g. its codec selection was superseded).
        _ = abandoned;

        gate.SetResult([48000]);

        Assert.That(await joiner, Is.EqualTo(new[] { 48000 }));
    }
}
