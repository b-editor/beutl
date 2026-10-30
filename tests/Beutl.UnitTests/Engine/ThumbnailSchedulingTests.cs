using System.Collections.Concurrent;
using Beutl.Audio;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.Threading;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.Engine;

[TestFixture, NonParallelizable]
public class ThumbnailSchedulingTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(5);

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task SlowDecoderOpen_LeavesConsumerResponsive_AndReleasesReader(bool video, bool cancel)
    {
        using var decoder = new DecoderScope(blockOpen: true);
        using var cancellation = new CancellationTokenSource();
        Dispatcher consumer = Dispatcher.Spawn();
        Task<Task<int>>? start = null;
        try
        {
            start = consumer.InvokeAsync<Task<int>>(() => ConsumeAsync(
                CreateProvider(decoder.Path, video), video, cancellation.Token));
            await decoder.OpenStarted.Task.WaitAsync(s_timeout);

            // The decoder is still blocked. The consumer must already have returned from MoveNext
            // and be able to process another operation, as the UI must during scrolling.
            Task<int> generation = await start.WaitAsync(s_timeout);
            Assert.That(await consumer.InvokeAsync(() => true).WaitAsync(s_timeout), Is.True);
            Assert.That(generation.IsCompleted, Is.False);

            if (cancel)
                cancellation.Cancel();
            decoder.ReleaseOpen.Set();

            Assert.That(await generation.WaitAsync(s_timeout), Is.EqualTo(video || cancel ? 0 : 1));
            Assert.That(decoder.Readers, Has.Count.EqualTo(1));
            Assert.That(decoder.Readers, Has.All.Matches<MediaReader>(r => r.IsDisposed));
        }
        finally
        {
            decoder.ReleaseOpen.Set();
            cancellation.Cancel();
            try
            {
                if (start is not null)
                    await start.Unwrap().WaitAsync(s_timeout);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                consumer.Shutdown();
                Assert.That(consumer.Thread.Join(s_timeout), Is.True);
            }
        }
    }

    [Test]
    public async Task Waveforms_BoundLiveReaders_CancelQueuedWork_AndResumeAfterEarlyDisposal()
    {
        using var decoder = new DecoderScope(blockOpen: false);
        using var cancellation = new CancellationTokenSource();
        await using var first = CreateProvider(decoder.Path, video: false)
            .GetWaveformChunksAsync(2, 4096, null).GetAsyncEnumerator();
        await using var second = CreateProvider(decoder.Path, video: false)
            .GetWaveformChunksAsync(2, 4096, null).GetAsyncEnumerator();
        await using var waiting = CreateProvider(decoder.Path, video: false)
            .GetWaveformChunksAsync(2, 4096, null, cancellation.Token).GetAsyncEnumerator();

        Assert.That(await first.MoveNextAsync().AsTask().WaitAsync(s_timeout), Is.True);
        Assert.That(await second.MoveNextAsync().AsTask().WaitAsync(s_timeout), Is.True);
        Task<bool> queued = waiting.MoveNextAsync().AsTask();
        Assert.That(queued.IsCompleted, Is.False);
        Assert.That(decoder.Readers.Count(r => !r.IsDisposed), Is.EqualTo(2));

        cancellation.Cancel();
        Assert.That(await queued.WaitAsync(s_timeout), Is.False);
        Assert.That(decoder.Readers, Has.Count.EqualTo(2), "cancelled work must not open a decoder");

        await first.DisposeAsync();
        Assert.That(decoder.Readers.Count(r => !r.IsDisposed), Is.EqualTo(1));
        await using (var resumed = CreateProvider(decoder.Path, video: false)
                         .GetWaveformChunksAsync(1, 4096, null).GetAsyncEnumerator())
        {
            Assert.That(await resumed.MoveNextAsync().AsTask().WaitAsync(s_timeout), Is.True);
        }

        await second.DisposeAsync();
        Assert.That(decoder.Readers, Has.Count.EqualTo(3));
        Assert.That(decoder.Readers, Has.All.Matches<MediaReader>(r => r.IsDisposed));
    }

    private static IThumbnailsProvider CreateProvider(string path, bool video)
    {
        var range = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1));
        if (video)
        {
            var source = new VideoSource();
            source.ReadFrom(new Uri(path));
            return new SourceVideo { Source = { Value = source }, TimeRange = { Value = range } };
        }

        var audioSource = new SoundSource();
        audioSource.ReadFrom(new Uri(path));
        return new SourceSound { Source = { Value = audioSource }, TimeRange = { Value = range } };
    }

    private static async Task<int> ConsumeAsync(IThumbnailsProvider provider, bool video, CancellationToken token)
    {
        int count = 0;
        if (video)
        {
            // Exercise decoder initialization and cleanup without requiring GPU rasterization.
            await foreach (var (_, _, thumbnail) in provider.GetThumbnailStripAsync(
                               100, 25, null, token, startIndex: 1, endIndex: 0))
            {
                thumbnail.Dispose();
                count++;
            }
        }
        else
        {
            await foreach (var _ in provider.GetWaveformChunksAsync(1, 4096, null, token))
                count++;
        }

        return count;
    }

    private sealed class DecoderScope : IDecoderInfo, IDisposable
    {
        public DecoderScope(bool blockOpen)
        {
            ReleaseOpen = new ManualResetEventSlim(!blockOpen);
            DecoderRegistry.Register(this);
        }

        public string Path { get; } = System.IO.Path.GetTempFileName();
        public string Name => nameof(ThumbnailSchedulingTests);
        public TaskCompletionSource OpenStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim ReleaseOpen { get; }
        public ConcurrentQueue<MediaReader> Readers { get; } = new();

        public bool IsSupported(string file) => file == Path;
        public IEnumerable<string> VideoExtensions() => [".tmp"];
        public IEnumerable<string> AudioExtensions() => [".tmp"];

        public MediaReader? Open(string file, MediaOptions options)
        {
            if (!IsSupported(file))
                return null;
            OpenStarted.TrySetResult();
            if (!ReleaseOpen.Wait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException("The test did not release decoder initialization.");
            MediaReader reader = options.StreamsToLoad == MediaMode.Video
                ? new TestMediaReader(new PixelSize(64, 36), new Rational(30), 30)
                : new TestAudioReader(44100, 2, MediaTime.FromSeconds(1));
            Readers.Enqueue(reader);
            return reader;
        }

        public void Dispose()
        {
            DecoderRegistry.Unregister(this);
            ReleaseOpen.Dispose();
            File.Delete(Path);
        }
    }
}
