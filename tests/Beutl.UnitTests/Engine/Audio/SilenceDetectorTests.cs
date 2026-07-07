using Beutl.Audio;
using Beutl.Engine;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class SilenceDetectorTests
{
    private const double ThresholdDb = -40.0;
    private static readonly TimeSpan TotalDuration = TimeSpan.FromSeconds(10);
    private const int ChunkCount = 100;

    // peak 0.5 -> well above -40dB (linear 0.01); peak 0 -> silence.
    private static WaveformChunk Loud(int i) => new(i, -0.5f, 0.5f);
    private static WaveformChunk Silent(int i) => new(i, 0f, 0f);

    private static SilenceDetectionOptions Options(double minSilenceSecs, double paddingSecs, double thresholdDb = ThresholdDb)
        => new(thresholdDb, TimeSpan.FromSeconds(minSilenceSecs), TimeSpan.FromSeconds(paddingSecs));

    private static WaveformChunk[] BuildChunks(int fromInclusive, int toExclusive, Func<int, WaveformChunk> make)
    {
        var chunks = new WaveformChunk[toExclusive - fromInclusive];
        for (int i = fromInclusive; i < toExclusive; i++)
            chunks[i - fromInclusive] = make(i);
        return chunks;
    }

    [Test]
    public void EmptyChunks_ReturnsNoRegions()
    {
        var regions = SilenceDetector.Detect([], TotalDuration, ChunkCount, Options(0.3, 0.1));

        Assert.That(regions, Is.Empty);
    }

    [Test]
    public void ZeroDuration_ReturnsNoRegions()
    {
        var chunks = BuildChunks(0, ChunkCount, Silent);

        var regions = SilenceDetector.Detect(chunks, TimeSpan.Zero, ChunkCount, Options(0.3, 0.1));

        Assert.That(regions, Is.Empty);
    }

    [Test]
    public void AllLoud_ReturnsNoRegions()
    {
        var chunks = BuildChunks(0, ChunkCount, Loud);

        var regions = SilenceDetector.Detect(chunks, TotalDuration, ChunkCount, Options(0.3, 0.1));

        Assert.That(regions, Is.Empty);
    }

    [Test]
    public void AllSilent_ReturnsSingleRegionSpanningWholeDurationMinusPadding()
    {
        var chunks = BuildChunks(0, ChunkCount, Silent);

        var regions = SilenceDetector.Detect(chunks, TotalDuration, ChunkCount, Options(0.3, 0.1));

        Assert.That(regions, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(regions[0].Start, Is.EqualTo(TimeSpan.FromSeconds(0.1)));
            Assert.That(regions[0].End, Is.EqualTo(TimeSpan.FromSeconds(9.9)));
        });
    }

    [Test]
    public void SingleSilenceRunInMiddle_ReturnsPaddedRegion()
    {
        // chunks 20..39 silent -> raw [2.0s, 4.0s]; padding 100ms -> [2.1, 3.9].
        var chunks = new List<WaveformChunk>();
        chunks.AddRange(BuildChunks(0, 20, Loud));
        chunks.AddRange(BuildChunks(20, 40, Silent));
        chunks.AddRange(BuildChunks(40, ChunkCount, Loud));

        var regions = SilenceDetector.Detect(chunks, TotalDuration, ChunkCount, Options(0.3, 0.1));

        Assert.That(regions, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(regions[0].Start, Is.EqualTo(TimeSpan.FromSeconds(2.1)));
            Assert.That(regions[0].End, Is.EqualTo(TimeSpan.FromSeconds(3.9)));
        });
    }

    [Test]
    public void MultipleSilenceRuns_ReturnsOneRegionPerRun()
    {
        var chunks = new List<WaveformChunk>();
        chunks.AddRange(BuildChunks(0, 10, Loud));
        chunks.AddRange(BuildChunks(10, 20, Silent)); // [1.0, 2.0] -> padded [1.1, 1.9]
        chunks.AddRange(BuildChunks(20, 30, Loud));
        chunks.AddRange(BuildChunks(30, 40, Silent)); // [3.0, 4.0] -> padded [3.1, 3.9]
        chunks.AddRange(BuildChunks(40, ChunkCount, Loud));

        var regions = SilenceDetector.Detect(chunks, TotalDuration, ChunkCount, Options(0.3, 0.1));

        Assert.That(regions, Has.Count.EqualTo(2));
        Assert.Multiple(() =>
        {
            Assert.That(regions[0].Start, Is.EqualTo(TimeSpan.FromSeconds(1.1)));
            Assert.That(regions[0].End, Is.EqualTo(TimeSpan.FromSeconds(1.9)));
            Assert.That(regions[1].Start, Is.EqualTo(TimeSpan.FromSeconds(3.1)));
            Assert.That(regions[1].End, Is.EqualTo(TimeSpan.FromSeconds(3.9)));
        });
    }

    [Test]
    public void RunShorterThanMinSilenceDuration_IsDropped()
    {
        // 3 chunks = 0.3s; min silence 0.5s -> dropped.
        var chunks = new List<WaveformChunk>();
        chunks.AddRange(BuildChunks(0, 20, Loud));
        chunks.AddRange(BuildChunks(20, 23, Silent));
        chunks.AddRange(BuildChunks(23, ChunkCount, Loud));

        var regions = SilenceDetector.Detect(chunks, TotalDuration, ChunkCount, Options(0.5, 0.0));

        Assert.That(regions, Is.Empty);
    }

    [Test]
    public void RunAtExactlyMinSilenceDuration_IsKept()
    {
        // 5 chunks = 0.5s; min silence 0.5s -> kept (>= comparison). No padding.
        var chunks = new List<WaveformChunk>();
        chunks.AddRange(BuildChunks(0, 20, Loud));
        chunks.AddRange(BuildChunks(20, 25, Silent));
        chunks.AddRange(BuildChunks(25, ChunkCount, Loud));

        var regions = SilenceDetector.Detect(chunks, TotalDuration, ChunkCount, Options(0.5, 0.0));

        Assert.That(regions, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(regions[0].Start, Is.EqualTo(TimeSpan.FromSeconds(2.0)));
            Assert.That(regions[0].End, Is.EqualTo(TimeSpan.FromSeconds(2.5)));
        });
    }

    [Test]
    public void PaddingLargerThanRun_DropsRegion()
    {
        // 5 chunks = 0.5s, padding 0.3s -> padded [2.3, 2.2] -> end <= start -> dropped.
        var chunks = new List<WaveformChunk>();
        chunks.AddRange(BuildChunks(0, 20, Loud));
        chunks.AddRange(BuildChunks(20, 25, Silent));
        chunks.AddRange(BuildChunks(25, ChunkCount, Loud));

        var regions = SilenceDetector.Detect(chunks, TotalDuration, ChunkCount, Options(0.1, 0.3));

        Assert.That(regions, Is.Empty);
    }

    [Test]
    public void MissingChunks_TreatedAsSilent()
    {
        // Provide loud chunks 0..9 and 20..29; chunkCount 30. Chunks 10..19 are missing and
        // must be treated as silent -> one region [1.0s, 2.0s] (no padding).
        var chunks = new List<WaveformChunk>();
        chunks.AddRange(BuildChunks(0, 10, Loud));
        chunks.AddRange(BuildChunks(20, 30, Loud));

        var regions = SilenceDetector.Detect(chunks, TimeSpan.FromSeconds(3), 30, Options(0.3, 0.0));

        Assert.That(regions, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(regions[0].Start, Is.EqualTo(TimeSpan.FromSeconds(1.0)));
            Assert.That(regions[0].End, Is.EqualTo(TimeSpan.FromSeconds(2.0)));
        });
    }

    [Test]
    public void PeakBelowThreshold_IsSilent_PeakAbove_IsLoud()
    {
        // -40dB -> linear 0.01. peak 0.005 (< 0.01) is silent; peak 0.02 (> 0.01) is loud.
        var chunks = new WaveformChunk[ChunkCount];
        for (int i = 0; i < ChunkCount; i++)
            chunks[i] = i < 50 ? new WaveformChunk(i, -0.005f, 0.005f) : new WaveformChunk(i, -0.02f, 0.02f);

        var regions = SilenceDetector.Detect(chunks, TotalDuration, ChunkCount, Options(0.3, 0.0));

        Assert.That(regions, Has.Count.EqualTo(1));
        // Silent run is chunks 0..49 -> [0, 5.0s].
        Assert.Multiple(() =>
        {
            Assert.That(regions[0].Start, Is.EqualTo(TimeSpan.Zero));
            Assert.That(regions[0].End, Is.EqualTo(TimeSpan.FromSeconds(5.0)));
        });
    }

    [Test]
    public void ZeroPadding_KeepsRawRegion()
    {
        var chunks = new List<WaveformChunk>();
        chunks.AddRange(BuildChunks(0, 20, Loud));
        chunks.AddRange(BuildChunks(20, 40, Silent));
        chunks.AddRange(BuildChunks(40, ChunkCount, Loud));

        var regions = SilenceDetector.Detect(chunks, TotalDuration, ChunkCount, Options(0.3, 0.0));

        Assert.That(regions, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(regions[0].Start, Is.EqualTo(TimeSpan.FromSeconds(2.0)));
            Assert.That(regions[0].End, Is.EqualTo(TimeSpan.FromSeconds(4.0)));
        });
    }

    [TestCase(0, Description = "chunkCount must be positive")]
    [TestCase(-1, Description = "negative chunkCount")]
    public void InvalidChunkCount_Throws(int chunkCount)
    {
        var chunks = BuildChunks(0, 10, Silent);

        Assert.Throws<ArgumentOutOfRangeException>(() => SilenceDetector.Detect(
            chunks, TotalDuration, chunkCount, Options(0.3, 0.0)));
    }

    [Test]
    public void NullChunks_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SilenceDetector.Detect(
            null!, TotalDuration, ChunkCount, Options(0.3, 0.0)));
    }

    [Test]
    public void NegativeMinSilenceDuration_Throws()
    {
        var chunks = BuildChunks(0, 10, Silent);

        Assert.Throws<ArgumentOutOfRangeException>(() => SilenceDetector.Detect(
            chunks, TotalDuration, ChunkCount, Options(-0.1, 0.0)));
    }

    [Test]
    public void NegativePadding_Throws()
    {
        var chunks = BuildChunks(0, 10, Silent);

        Assert.Throws<ArgumentOutOfRangeException>(() => SilenceDetector.Detect(
            chunks, TotalDuration, ChunkCount, Options(0.3, -0.1)));
    }
}
