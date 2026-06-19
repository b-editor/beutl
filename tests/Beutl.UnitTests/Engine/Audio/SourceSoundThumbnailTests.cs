using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class SourceSoundThumbnailTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp() => TestMediaHelper.RegisterTestDecoder();

    [TestCase(1, 44100)]
    [TestCase(44, 44100)]
    [TestCase(45, 44100)]
    [TestCase(1024, 48000)]
    public void GetWaveformChunkDuration_MapsBackToRequestedSampleCount(int sampleCount, int sampleRate)
    {
        TimeSpan duration = SourceSound.GetWaveformChunkDuration(sampleCount, sampleRate);

        Assert.That(
            AudioProcessContext.GetSampleCount(new TimeRange(TimeSpan.Zero, duration), sampleRate),
            Is.EqualTo(sampleCount));
    }

    [Test]
    public void GetWaveformChunkDuration_AdjacentBoundariesDifferByAtMostOneTick()
    {
        const int sampleRate = 44100;
        const int totalSamples = 44100;
        const int chunkCount = 1000;

        for (int chunkIndex = 0; chunkIndex < chunkCount - 1; chunkIndex++)
        {
            int startSample = (int)((long)chunkIndex * totalSamples / chunkCount);
            int endSample = (int)((long)(chunkIndex + 1) * totalSamples / chunkCount);
            var start = TimeSpan.FromSeconds((double)startSample / sampleRate);
            var duration = SourceSound.GetWaveformChunkDuration(endSample - startSample, sampleRate);
            var nextStart = TimeSpan.FromSeconds((double)endSample / sampleRate);

            Assert.That(Math.Abs((start + duration - nextStart).Ticks), Is.LessThanOrEqualTo(1));
        }
    }

    // Pins the contiguity contract of GetWaveformChunksAsync: consecutive Compose ranges are
    // tick-contiguous (so a stateful effect carries state across the strip) only when each chunk's
    // full span fits within samplesPerChunk. When fullSpan exceeds it the chunk composes a prefix
    // and a gap opens between ranges — the effect restarts per chunk, an accepted approximation.
    [TestCase(48000, 48000, 1000, 4096, true)]   // 1s over 1000 chunks: fullSpan=48 <= 4096 -> contiguous
    [TestCase(48000, 2880000, 500, 4096, false)] // 60s over 500 chunks: fullSpan=5760 > 4096 -> gap
    public void WaveformChunkRanges_AreContiguous_OnlyWhenSpanFitsChunkBudget(
        int sampleRate, int totalSamples, int chunkCount, int samplesPerChunk, bool expectContiguous)
    {
        bool sawGap = false;
        for (int chunkIndex = 0; chunkIndex < chunkCount - 1; chunkIndex++)
        {
            int startSample = (int)((long)chunkIndex * totalSamples / chunkCount);
            int endSample = (int)((long)(chunkIndex + 1) * totalSamples / chunkCount);
            int sampleCount = Math.Min(endSample - startSample, samplesPerChunk);

            var start = TimeSpan.FromSeconds((double)startSample / sampleRate);
            var duration = SourceSound.GetWaveformChunkDuration(sampleCount, sampleRate);
            var nextStart = TimeSpan.FromSeconds((double)endSample / sampleRate);

            if (Math.Abs((start + duration - nextStart).Ticks) > 1)
                sawGap = true;
        }

        Assert.That(sawGap, Is.EqualTo(!expectContiguous));
    }

    // Cache-control-flow contract of GetWaveformChunksAsync: a miss computes and saves the chunk, a
    // hit returns the cached value and is not re-saved, and every chunk is still produced either way.
    [Test]
    public async Task GetWaveformChunks_CacheMissComputesAndSaves_CacheHitReturnsCachedAndSkipsSave()
    {
        const int chunkCount = 50;
        const int samplesPerChunk = 4096; // > per-chunk span (88200/50 = 1764) so ranges are contiguous

        string path = TestMediaHelper.CreateTestAudioFile(sampleRate: 44100, channels: 2, durationSeconds: 2.0);
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(path));

        var sound = new SourceSound
        {
            Source = { CurrentValue = soundSource },
            TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(2)),
        };

        // Cold run: empty cache. Every chunk is a miss, computed from audio and saved.
        var coldCache = new FakeWaveformCache();
        var coldChunks = await CollectAsync(sound, chunkCount, samplesPerChunk, coldCache);

        Assert.That(coldChunks, Has.Count.EqualTo(chunkCount), "every chunk in range should be produced");
        Assert.That(coldCache.SaveCount, Is.EqualTo(chunkCount), "each cache miss should save its waveform");
        Assert.That(coldChunks.Select(c => c.Index), Is.EqualTo(Enumerable.Range(0, chunkCount)));

        // Warm run: cache reports a hit for every query with a fixed sentinel. Each chunk must come
        // back with the sentinel value and nothing must be re-saved.
        const float sentinelMin = -0.25f;
        const float sentinelMax = 0.75f;
        var warmCache = new FakeWaveformCache(sentinelMin, sentinelMax);
        var warmChunks = await CollectAsync(sound, chunkCount, samplesPerChunk, warmCache);

        Assert.That(warmChunks, Has.Count.EqualTo(chunkCount));
        Assert.That(warmCache.SaveCount, Is.Zero, "cache hits must not be re-saved");
        Assert.That(warmChunks, Is.All.Matches<WaveformChunk>(
            c => c.MinValue == sentinelMin && c.MaxValue == sentinelMax),
            "a cache hit must return the cached min/max verbatim");
    }

    private static async Task<List<WaveformChunk>> CollectAsync(
        SourceSound sound, int chunkCount, int samplesPerChunk, IThumbnailCacheService cache)
    {
        var chunks = new List<WaveformChunk>();
        await foreach (var chunk in sound.GetWaveformChunksAsync(chunkCount, samplesPerChunk, cache))
            chunks.Add(chunk);
        return chunks;
    }

    // Returns a hit (with the configured sentinel) for every waveform query when sentinels are set;
    // otherwise always misses. Counts SaveWaveform calls so tests can assert the !cacheHit guard.
    private sealed class FakeWaveformCache(float? hitMin = null, float? hitMax = null) : IThumbnailCacheService
    {
        public int SaveCount { get; private set; }

        public bool TryGetWaveform(string cacheKey, TimeSpan time, TimeSpan threshold, out float minValue, out float maxValue)
        {
            if (hitMin is { } min && hitMax is { } max)
            {
                minValue = min;
                maxValue = max;
                return true;
            }

            minValue = 0f;
            maxValue = 0f;
            return false;
        }

        public void SaveWaveform(string cacheKey, TimeSpan time, float minValue, float maxValue) => SaveCount++;

        public bool TryGet(string cacheKey, TimeSpan time, TimeSpan threshold, out Bitmap? bitmap)
        {
            bitmap = null;
            return false;
        }

        public void Save(string cacheKey, TimeSpan time, Bitmap bitmap) => throw new NotSupportedException();

        public void Invalidate(string cacheKey)
        {
        }
    }
}
