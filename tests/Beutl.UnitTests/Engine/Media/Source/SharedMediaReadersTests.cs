using System.Diagnostics.CodeAnalysis;
using Beutl.Audio;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.Media.Source;

namespace Beutl.UnitTests.Engine.Media.Source;

[TestFixture]
public class SharedMediaReadersTests
{
    private static readonly SharedMediaReaders.Key s_key = new("/media/recording.mp4", MediaMode.Video, false, default, 0);
    private readonly List<FakeReader> _opened = [];
    private long _now;
    private SharedMediaReaders _pool = null!;

    [SetUp]
    public void SetUp()
    {
        _opened.Clear();
        _now = 0;
        _pool = new SharedMediaReaders(() => _now);
    }

    private MediaReader Open(SharedMediaReaders.Key? key = null, Func<MediaReader, bool>? shareable = null)
        => _pool.Open(key ?? s_key, () =>
        {
            var reader = new FakeReader();
            _opened.Add(reader);
            return reader;
        }, shareable);

    private void Read(MediaReader handle, int from, int count)
    {
        for (int frame = from; frame < from + count; frame++)
        {
            handle.ReadVideo(frame, out _);
            _now += 16;
        }
    }

    [Test]
    public void PiecesOfASplitClip_ContinueOnTheReaderThePreviousPieceLeft()
    {
        using MediaReader first = Open();
        using MediaReader second = Open();

        Read(first, 0, 60);
        // The removed silence spans a few frames; the next piece resumes just after the previous one ended.
        Read(second, 66, 60);

        Assert.That(_opened, Has.Count.EqualTo(1));
        Assert.That(_opened[0].Seeks, Is.EqualTo(1), "only the first read positions the reader");
    }

    [Test]
    public void ANewcomerAfterALongGap_SeeksTheReaderInsteadOfOpeningAnother()
    {
        using MediaReader first = Open();
        using MediaReader second = Open();

        Read(first, 0, 30);
        Read(second, 5000, 30);

        Assert.That(_opened, Has.Count.EqualTo(1));
        Assert.That(_opened[0].Seeks, Is.EqualTo(2));
    }

    [Test]
    public void ConcurrentConsumers_GetTheirOwnReadersAndStopSeeking()
    {
        using MediaReader top = Open();
        using MediaReader bottom = Open();

        for (int i = 0; i < 40; i++)
        {
            Read(top, i, 1);
            Read(bottom, 5000 + i, 1);
        }

        Assert.That(_opened, Has.Count.EqualTo(2));
        Assert.That(_opened.Sum(reader => reader.Seeks), Is.LessThanOrEqualTo(4), "readers must not ping-pong between positions");
    }

    [Test]
    public void AtMostThreeReaders_OpenForOneFile()
    {
        MediaReader[] handles = Enumerable.Range(0, 5).Select(_ => Open()).ToArray();
        try
        {
            for (int i = 0; i < 20; i++)
                for (int h = 0; h < handles.Length; h++)
                    Read(handles[h], h * 10_000 + i, 1);

            Assert.That(_opened, Has.Count.EqualTo(SharedMediaReaders.MaxReaders));
        }
        finally
        {
            foreach (MediaReader handle in handles) handle.Dispose();
        }
    }

    [Test]
    public void AnIdleConsumer_LendsItsReader()
    {
        using MediaReader top = Open();
        using MediaReader bottom = Open();
        Read(top, 0, 10);
        Read(bottom, 5000, 10);
        Read(top, 10, 10);
        Assert.That(_opened, Has.Count.EqualTo(2));

        // Both pause; a third consumer then reuses the least recently used reader.
        _now += SharedMediaReaders.BusyMilliseconds + 1;
        using MediaReader third = Open();
        Read(third, 9000, 10);

        Assert.That(_opened, Has.Count.EqualTo(2));
    }

    [Test]
    public void ReleasingEveryHandle_ClosesTheReadersAndALaterOpenStartsFresh()
    {
        MediaReader first = Open();
        MediaReader second = Open();
        Read(first, 0, 5);

        first.Dispose();
        Assert.That(_opened[0].IsDisposed, Is.False, "the second handle still reads it");
        second.Dispose();
        Assert.That(_opened[0].IsDisposed, Is.True);
        Assert.That(_pool.OpenGroupCount, Is.Zero);

        using MediaReader third = Open();
        Assert.That(_opened, Has.Count.EqualTo(2));
    }

    [Test]
    public void Invalidation_StartsFreshReadersWhileOldHandlesKeepTheirs()
    {
        using MediaReader before = Open();
        _pool.Invalidate(s_key.Path);
        using MediaReader after = Open();

        Read(before, 0, 1);
        Read(after, 0, 1);

        Assert.That(_opened, Has.Count.EqualTo(2));
        Assert.That(_opened.All(reader => reader.Reads == 1), Is.True);
    }

    [Test]
    public void DifferentKeys_DoNotShare()
    {
        using MediaReader original = Open();
        using MediaReader proxy = Open(s_key with { PreferProxy = true, Preset = ProxyPreset.Quarter });
        using MediaReader audio = Open(s_key with { Mode = MediaMode.Audio });

        Assert.That(_opened, Has.Count.EqualTo(3));
    }

    [Test]
    public void AFailedOpen_IsNotCached()
    {
        Assert.That(() => _pool.Open(s_key, () => throw new IOException("unreadable")), Throws.InstanceOf<IOException>());

        using MediaReader handle = Open();
        Assert.That(_opened, Has.Count.EqualTo(1));
        Assert.That(handle.HasVideo, Is.True);
    }

    [Test]
    public async Task ASlowOpen_DoesNotHoldUpOtherFilesOrJoinersOfOtherFiles()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<MediaReader> slow = Task.Run(() => _pool.Open(s_key, () =>
        {
            started.SetResult();
            release.Wait(TimeSpan.FromSeconds(15));
            return new FakeReader();
        }));
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // A joiner of the slow file waits for its first reader without blocking the pool.
            Task<MediaReader> joiner = Task.Run(() => Open());
            await Task.Delay(50);

            using MediaReader other = await Task.Run(() => Open(s_key with { Path = "/media/other.mp4" }))
                .WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(joiner.IsCompleted, Is.False);

            release.Set();
            using MediaReader first = await slow.WaitAsync(TimeSpan.FromSeconds(10));
            using MediaReader joined = await joiner.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(SharedMediaReaders.UnderlyingOf(joined), Is.SameAs(SharedMediaReaders.UnderlyingOf(first)));
        }
        finally
        {
            release.Set();
        }
    }

    [Test]
    public void AnUnshareableFirstReader_ServesOnlyItsHandle()
    {
        using MediaReader fallback = Open(shareable: _ => false);
        using MediaReader next = Open();

        Assert.That(_opened, Has.Count.EqualTo(2));
    }

    [Test]
    public void AudioPieces_ContinueBySamplePosition()
    {
        SharedMediaReaders.Key key = s_key with { Mode = MediaMode.Audio };
        using MediaReader first = Open(key);
        using MediaReader second = Open(key);

        for (int start = 0; start < 48_000; start += 4800) first.ReadAudio(start, 4800, out _);
        for (int start = 52_800; start < 96_000; start += 4800) second.ReadAudio(start, 4800, out _);

        Assert.That(_opened, Has.Count.EqualTo(1));
        Assert.That(_opened[0].Seeks, Is.EqualTo(1));
    }

    [Test]
    public void AHandle_ReportsTheFirstReadersStreams()
    {
        using MediaReader handle = Open();

        Assert.Multiple(() =>
        {
            Assert.That(handle.VideoInfo, Is.SameAs(_opened[0].VideoInfo));
            Assert.That(handle.AudioInfo, Is.SameAs(_opened[0].AudioInfo));
            Assert.That(SharedMediaReaders.UnderlyingOf(handle), Is.SameAs(_opened[0]));
        });
    }

    // Counts reads that cannot continue from the previous one, which a real decoder answers by seeking.
    private sealed class FakeReader : MediaReader
    {
        private long? _next;

        public int Seeks { get; private set; }

        public int Reads { get; private set; }

        public override VideoStreamInfo VideoInfo { get; } = new("fake", 100_000L, new PixelSize(16, 16), new Rational(30, 1));

        public override AudioStreamInfo AudioInfo { get; } = new("fake", new Rational(3600, 1), 48_000, 2);

        public override bool HasVideo => true;

        public override bool HasAudio => true;

        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            Track(frame, frame + 1, SharedMediaReaders.VideoContinuationFrames);
            image = null;
            return false;
        }

        public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out AudioBuffer? sound)
        {
            Track(start, start + length, AudioInfo.SampleRate);
            sound = null;
            return false;
        }

        private void Track(long position, long next, long continuation)
        {
            Reads++;
            if (_next is not { } expected || position < expected || position - expected > continuation) Seeks++;
            _next = next;
        }
    }
}
