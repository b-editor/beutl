using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Beutl.Audio;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class SourceSoundThumbnailTests
{
    [Test]
    public void CacheKey_IncludesChannelAssignmentsAndRestoresForAuto()
    {
        var sound = new SourceSound();
        string? automatic = sound.GetThumbnailsCacheKey();
        Assert.That(automatic, Is.Not.Null);
        sound.ChannelMap.Value = new AudioChannelMap(AudioChannelFormat.Stereo, AudioChannelLayout.GetDefault(4), [2, 3]);
        string? selected = sound.GetThumbnailsCacheKey();
        Assert.That(selected, Is.Not.EqualTo(automatic));
        sound.ChannelMap.Value = sound.ChannelMap.Value.WithSources([2, -1]);
        Assert.That(sound.GetThumbnailsCacheKey(), Is.Not.EqualTo(selected));
        sound.ChannelMap.Value = AudioChannelMap.Auto;
        Assert.That(sound.GetThumbnailsCacheKey(), Is.EqualTo(automatic));
    }

    // Cache-control-flow contract of GetWaveformChunksAsync: a miss computes and saves the chunk, a
    // hit returns the cached value and is not re-saved, and every chunk is still produced either way.
    [Test]
    public async Task GetWaveformChunks_CacheMissComputesAndSaves_CacheHitReturnsCachedAndSkipsSave()
    {
        const int chunkCount = 50;
        const int samplesPerChunk = 4096; // > per-chunk span (88200/50 = 1764) so ranges are contiguous

        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-test-audio-44100_2_2000.testaudio");
        File.WriteAllBytes(path, [1]);
        try
        {
            var soundSource = new SoundSource();
            soundSource.ReadFrom(new Uri(path));

            var sound = new SourceSound
            {
                Source = { Value = soundSource },
                TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(2)) },
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
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void PeakPyramidMatchesFullScanForAlignedAndPartialBlocks()
    {
        var random = new Random(73);
        float[] samples = Enumerable.Range(0, 3591).Select(_ => random.NextSingle() * 2 - 1).ToArray();
        var pyramid = new AudioPeakPyramid(samples);
        foreach (var (start, end) in new[] { (0, 3591), (64, 3072), (13, 1573), (63, 64), (3589, 3591) })
        {
            float[] window = samples[start..end];
            Assert.That(pyramid.Measure(start, end), Is.EqualTo((window.Min(), window.Max())));
        }
    }

    [Test]
    public async Task SourcePeaksMatchCompositionAndReuseSequentialReadsWhenTheRangeChanges()
    {
        using var decoder = new PeakDecoder();
        SourceSound sound = Clip(decoder.Path);
        SourceSound reference = Clip(decoder.SlowPath);
        List<WaveformChunk> expected = await Read(reference, 17, null);
        List<WaveformChunk> actual = await Read(sound, 17, null);
        Assert.That(actual, Is.EqualTo(expected), "static gain, pan, and channel assignment match composed samples");
        Assert.That(decoder.PeakReads, Is.EqualTo(new[] { (0, 8000), (8000, 8000) }));

        SourceSound trimmed = Clip(decoder.Path);
        trimmed.OffsetPosition.Value = MediaTime.FromSeconds(0.575);
        trimmed.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(8), MediaTime.FromSeconds(0.5));
        var visible = await Read(trimmed, 21, [0, 3, 20]);
        Assert.That(visible.Select(c => c.Index), Is.EqualTo(new[] { 0, 3, 20 }));
        Assert.That(decoder.PeakReads, Has.Count.EqualTo(2), "trim, split and zoom reuse the same source pages");

        File.WriteAllBytes(decoder.Path, [1, 2]);
        await Read(trimmed, 21, [0]);
        Assert.That(decoder.PeakReads, Has.Count.EqualTo(3), "a changed file fingerprint invalidates source peaks");

        SourceSound Clip(string path) => new()
        {
            Source = { Value = SoundSource.Open(path) },
            OffsetPosition = { Value = MediaTime.FromSeconds(0.25) },
            TimeRange = { Value = new TimeRange(MediaTime.FromSeconds(4), MediaTime.FromSeconds(1.5)) },
            Gain = { Value = 250 },
            Pan = { Value = -30 },
            ChannelMap = { Value = new AudioChannelMap(AudioChannelFormat.Stereo, AudioChannelLayout.Stereo, [1, 0]) },
        };

        async Task<List<WaveformChunk>> Read(SourceSound clip, int count, int[]? indices)
        {
            var chunks = new List<WaveformChunk>();
            await foreach (var chunk in clip.GetWaveformChunksAsync(count, 4096, null, indices, CancellationToken.None, decoder.Decoders))
                chunks.Add(chunk);
            return chunks;
        }
    }

    private sealed class PeakDecoder : IDecoderInfo, IDisposable
    {
        public PeakDecoder()
        {
            File.WriteAllBytes(Path, [1]);
            Decoders.Register(this);
        }

        public string Path { get; } = System.IO.Path.GetTempFileName();
        public string SlowPath { get; } = System.IO.Path.GetTempFileName();
        public DecoderRegistry Decoders { get; } = new(new());
        public List<(int Start, int Length)> PeakReads { get; } = [];
        public string Name => "Waveform peak test decoder";

        public bool IsSupported(string file) => file == Path || file == SlowPath;
        public IEnumerable<string> AudioExtensions() => [".tmp"];
        public IEnumerable<string> VideoExtensions() => [];
        public MediaReader Open(string file, MediaOptions options) => new Reader(file == Path ? PeakReads : null);

        public void Dispose()
        {
            File.Delete(Path);
            File.Delete(SlowPath);
        }

        private sealed class Reader(List<(int, int)>? reads) : MediaReader
        {
            public override VideoStreamInfo VideoInfo => throw new NotSupportedException();
            public override AudioStreamInfo AudioInfo { get; } = new("test", new Rational(2, 1), 8000, 2);
            public override bool HasVideo => false;
            public override bool HasAudio => true;

            public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
            {
                image = null;
                return false;
            }

            public override bool ReadAudio(int start, int length, out AudioBuffer sound)
            {
                reads?.Add((start, length));
                sound = new AudioBuffer(8000, 2, length);
                var left = sound.GetChannelData(0);
                var right = sound.GetChannelData(1);
                for (int i = 0; i < length; i++)
                {
                    left[i] = (start + i) % 729 / 400f - 0.9f;
                    right[i] = -left[i];
                }
                return true;
            }
        }
    }

    [Test]
    public async Task GetWaveformChunks_LongDurationAboveIntSampleBoundary_ProducesAllChunks()
    {
        const int sampleRate = 192000;
        const int chunkCount = 4;
        const int samplesPerChunk = 4096;
        var duration = MediaTime.FromSeconds(18000);
        string path = TestMediaHelper.CreateTestAudioFile(
            sampleRate: sampleRate,
            channels: 2,
            durationSeconds: duration.ToSeconds());
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(path));

        var sound = new SourceSound
        {
            Source = { Value = soundSource },
            TimeRange = { Value = new TimeRange(MediaTime.Zero, duration) },
        };

        var chunks = await CollectAsync(sound, chunkCount, samplesPerChunk, new FakeWaveformCache());

        Assert.That(chunks.Select(c => c.Index), Is.EqualTo(Enumerable.Range(0, chunkCount)));

        // The final chunk starts past sample index int.MaxValue: 5h @ 192kHz is 3,456,000,000 samples
        // and the last of 4 chunks begins at 2,592,000,000. SourceNode's int-range read guard then
        // composes it as silence instead of wrapping the offset to a wrong position.
        Assert.That(chunks[^1].MinValue, Is.Zero);
        Assert.That(chunks[^1].MaxValue, Is.Zero);
    }

    [Test]
    public void GetWaveformChunkSamplePosition_DoesNotOverflowIntermediateProduct()
    {
        const int chunkIndex = 2;
        const int chunkCount = 3;
        const long totalSamples = long.MaxValue;
        long expected = (long)((BigInteger)chunkIndex * totalSamples / chunkCount);

        long actual = SourceSound.GetWaveformChunkSamplePosition(chunkIndex, totalSamples, chunkCount);

        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(
            SourceSound.GetWaveformChunkSamplePosition(chunkCount, totalSamples, chunkCount),
            Is.EqualTo(totalSamples));
    }

    // Regression for #2183: a video-only source (no audio track) loads as an unreadable resource
    // (Duration == 0). TryGetOriginalDuration must return false instead of reporting a zero duration,
    // so timeline resize/trim code does not act on it (or crash dereferencing AudioInfo).
    [Test]
    public void TryGetOriginalDuration_VideoOnlySource_ReturnsFalse()
    {
        string path = TestMediaHelper.CreateTestVideoFile(80, 80, new Rational(30, 1), 60);
        var soundSource = new SoundSource();
        soundSource.ReadFrom(new Uri(path));
        var sound = new SourceSound
        {
            Source = { Value = soundSource }
        };

        bool result = sound.TryGetOriginalDuration(out MediaTime duration, decoders: TestMediaHelper.CreateRegistry());

        Assert.That(result, Is.False,
            "音声トラックのないソースは元の長さを持たない (#2183)");
        Assert.That(duration, Is.EqualTo(MediaTime.Zero));
    }

    private static async Task<List<WaveformChunk>> CollectAsync(
        SourceSound sound, int chunkCount, int samplesPerChunk, IThumbnailCacheService cache)
    {
        var chunks = new List<WaveformChunk>();
        await foreach (var chunk in sound.GetWaveformChunksAsync(chunkCount, samplesPerChunk, cache, decoders: TestMediaHelper.CreateRegistry()))
            chunks.Add(chunk);
        return chunks;
    }

    // Returns a hit (with the configured sentinel) for every waveform query when sentinels are set;
    // otherwise always misses. Counts SaveWaveform calls so tests can assert the !cacheHit guard.
    private sealed class FakeWaveformCache(float? hitMin = null, float? hitMax = null) : IThumbnailCacheService
    {
        public int SaveCount { get; private set; }

        public bool TryGetWaveform(string cacheKey, MediaTime time, MediaTime threshold, out float minValue, out float maxValue)
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

        public void SaveWaveform(string cacheKey, MediaTime time, float minValue, float maxValue) => SaveCount++;

        public bool TryGet(string cacheKey, MediaTime time, MediaTime threshold, out Bitmap? bitmap)
        {
            bitmap = null;
            return false;
        }

        public void Save(string cacheKey, MediaTime time, Bitmap bitmap) => throw new NotSupportedException();

        public void Invalidate(string cacheKey)
        {
        }
    }
}
