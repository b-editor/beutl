using System.Runtime.CompilerServices;
using Beutl.Audio;
using Beutl.Editor.Components.TimelineTab.Services;
using Beutl.Engine;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public class SilenceWaveformAnalysisTests
{
    [Test]
    public void FindAudioProviders_ReturnsEveryEnabledAudioProvider()
    {
        var element = new Element();
        var first = new SourceSound();
        var second = new SourceSound();
        var disabled = new SourceSound
        {
            IsEnabled = false,
        };
        element.Objects.Add(first);
        element.Objects.Add(second);
        element.Objects.Add(disabled);

        IReadOnlyList<IThumbnailsProvider> providers = SilenceWaveformAnalysis.FindAudioProviders(element);

        Assert.That(providers, Is.EqualTo(new IThumbnailsProvider[] { first, second }));
    }

    [Test]
    public async Task CollectConservativeChunks_RequestsCompleteUncachedWaveforms()
    {
        var provider = new RecordingProvider([new WaveformChunk(0, -0.25f, 0.75f)]);

        IReadOnlyList<WaveformChunk> chunks = await SilenceWaveformAnalysis
            .CollectConservativeChunksAsync([provider], 1, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(provider.RequestedSamplesPerChunk, Is.EqualTo(int.MaxValue));
            Assert.That(provider.RequestedCacheService, Is.Null);
            Assert.That(chunks, Is.EqualTo(new[] { new WaveformChunk(0, -0.75f, 0.75f) }));
        });
    }

    [Test]
    public async Task CollectConservativeChunks_SumsEveryProviderBeforeSilenceDetection()
    {
        var first = new RecordingProvider([new WaveformChunk(0, -0.006f, 0.006f)]);
        var second = new RecordingProvider([new WaveformChunk(0, -0.006f, 0.006f)]);

        IReadOnlyList<WaveformChunk> chunks = await SilenceWaveformAnalysis
            .CollectConservativeChunksAsync([first, second], 1, CancellationToken.None);
        IReadOnlyList<SilenceRegion> regions = SilenceDetector.Detect(
            chunks,
            TimeSpan.FromSeconds(1),
            1,
            new SilenceDetectionOptions(-40, TimeSpan.Zero, TimeSpan.Zero));

        Assert.Multiple(() =>
        {
            Assert.That(chunks[0].MaxValue, Is.EqualTo(0.012f).Within(0.000001f));
            Assert.That(regions, Is.Empty);
            Assert.That(first.WaveformRequestCount, Is.EqualTo(1));
            Assert.That(second.WaveformRequestCount, Is.EqualTo(1));
        });
    }

    private sealed class RecordingProvider(IReadOnlyList<WaveformChunk> chunks)
        : IThumbnailsProvider
    {
        public ThumbnailsKind ThumbnailsKind => ThumbnailsKind.Audio;

        public int RequestedSamplesPerChunk { get; private set; }

        public IThumbnailCacheService? RequestedCacheService { get; private set; }

        public int WaveformRequestCount { get; private set; }

        public event EventHandler? ThumbnailsInvalidated
        {
            add { }
            remove { }
        }

        public async IAsyncEnumerable<(int Index, int Count, Bitmap Thumbnail)> GetThumbnailStripAsync(
            int maxWidth,
            int maxHeight,
            IThumbnailCacheService? cacheService = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default,
            int startIndex = 0,
            int endIndex = -1)
        {
            await Task.CompletedTask;
            yield break;
        }

        public async IAsyncEnumerable<WaveformChunk> GetWaveformChunksAsync(
            int chunkCount,
            int samplesPerChunk,
            IThumbnailCacheService? cacheService,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            RequestedSamplesPerChunk = samplesPerChunk;
            RequestedCacheService = cacheService;
            WaveformRequestCount++;

            foreach (WaveformChunk chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunk;
            }

            await Task.CompletedTask;
        }
    }
}
