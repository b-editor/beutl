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
    // When set, the next open waits for it, signalling _openEntered first.
    private ManualResetEventSlim? _openGate;
    private TaskCompletionSource? _openEntered;
    // When set, the next open returns or throws this instead of a default reader.
    private Func<FakeReader>? _nextOpen;

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
            if (Interlocked.Exchange(ref _openGate, null) is { } gate)
            {
                _openEntered?.TrySetResult();
                gate.Wait(TimeSpan.FromSeconds(15));
            }
            var reader = Interlocked.Exchange(ref _nextOpen, null)?.Invoke() ?? new FakeReader();
            lock (_opened) _opened.Add(reader);
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
    public void ReadersOfOneFile_StopAtTheLimit()
    {
        MediaReader[] handles = Enumerable.Range(0, SharedMediaReaders.MaxReaders + 2).Select(_ => Open()).ToArray();
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
    public void AsManyConsumersAsTheLimit_EachKeepAReaderAndStopSeeking()
    {
        MediaReader[] handles = Enumerable.Range(0, SharedMediaReaders.MaxReaders).Select(_ => Open()).ToArray();
        try
        {
            // The first rounds hand each consumer a reader of its own.
            for (int i = 0; i < 2; i++)
                for (int h = 0; h < handles.Length; h++)
                    Read(handles[h], h * 10_000 + i, 1);
            int warmUpSeeks = _opened.Sum(reader => reader.Seeks);

            for (int i = 2; i < 20; i++)
                for (int h = 0; h < handles.Length; h++)
                    Read(handles[h], h * 10_000 + i, 1);

            Assert.That(_opened, Has.Count.EqualTo(SharedMediaReaders.MaxReaders));
            Assert.That(_opened.Sum(reader => reader.Seeks), Is.EqualTo(warmUpSeeks));
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
    public void AnInvalidatedGroup_KeepsToTheReadersItHas()
    {
        using MediaReader top = Open();
        using MediaReader bottom = Open();
        Read(top, 0, 1);
        _pool.Invalidate(s_key.Path);

        // The consumers contend, but another reader could read the replaced file.
        Read(bottom, 5000, 1);
        Read(top, 1, 1);

        Assert.That(_opened, Has.Count.EqualTo(1));
        Assert.That(_opened[0].Reads, Is.EqualTo(3));
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

    // Top and bottom end up on separate readers: bottom took the first one, so top opened another.
    private (MediaReader Top, MediaReader Bottom) SplitOntoTwoReaders()
    {
        MediaReader top = Open();
        MediaReader bottom = Open();
        Read(top, 0, 1);
        Read(bottom, 5000, 1);
        Read(top, 1, 1);
        Assert.That(_opened, Has.Count.EqualTo(2));
        return (top, bottom);
    }

    [Test]
    public async Task ReadersOfOneFile_DecodeInParallel()
    {
        (MediaReader top, MediaReader bottom) = SplitOntoTwoReaders();
        using (top)
        using (bottom)
        using (var gate = new ManualResetEventSlim())
        {
            FakeReader blocked = _opened[0];
            blocked.Entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            blocked.Gate = gate;
            Task slow = Task.Run(() => bottom.ReadVideo(5001, out _));
            await blocked.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Task fast = Task.Run(() => top.ReadVideo(2, out _));
            Assert.That(await Task.WhenAny(fast, Task.Delay(TimeSpan.FromSeconds(5))), Is.SameAs(fast),
                "a read on another reader must not wait for a slow decode");
            gate.Set();
            await slow.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Test]
    public async Task OpeningAnotherReader_DoesNotHoldUpReadersOfTheFile()
    {
        using MediaReader top = Open();
        using MediaReader bottom = Open();
        Read(top, 0, 1);
        Read(bottom, 5000, 1);
        using var gate = new ManualResetEventSlim();
        _openEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _openGate = gate;

        // Top lost its reader to bottom, which is still reading, so top opens another one.
        Task opening = Task.Run(() => top.ReadVideo(1, out _));
        await _openEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Task reading = Task.Run(() => bottom.ReadVideo(5001, out _));
        Assert.That(await Task.WhenAny(reading, Task.Delay(TimeSpan.FromSeconds(5))), Is.SameAs(reading),
            "the file's other readers keep serving while one opens");
        gate.Set();
        await opening.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(_opened, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ClosingWhileAnotherReaderOpens_DisposesTheNewReader()
    {
        MediaReader top = Open();
        MediaReader bottom = Open();
        Read(top, 0, 1);
        Read(bottom, 5000, 1);
        using var gate = new ManualResetEventSlim();
        _openEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _openGate = gate;

        Task<bool> opening = Task.Run(() => top.ReadVideo(1, out _));
        await _openEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        top.Dispose();
        bottom.Dispose();
        gate.Set();

        Assert.That(await opening.WaitAsync(TimeSpan.FromSeconds(10)), Is.False);
        Assert.That(_opened, Has.Count.EqualTo(2));
        Assert.That(_opened, Has.All.Matches<FakeReader>(reader => reader.IsDisposed));
    }

    [Test]
    public async Task InvalidatingWhileAnotherReaderOpens_DisposesTheNewReader()
    {
        using MediaReader top = Open();
        using MediaReader bottom = Open();
        Read(top, 0, 1);
        Read(bottom, 5000, 1);
        using var gate = new ManualResetEventSlim();
        _openEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _openGate = gate;

        Task<bool> opening = Task.Run(() => top.ReadVideo(1, out _));
        await _openEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        _pool.Invalidate(s_key.Path);
        gate.Set();
        await opening.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(_opened, Has.Count.EqualTo(2));
        Assert.That(_opened[1].IsDisposed, Is.True);
        Assert.That(_opened[0].Reads, Is.EqualTo(3), "the read falls back to the open reader");
    }

    [Test]
    public void AnExtraReaderThatFailsToOpen_FallsBackToAnOpenReader()
    {
        using MediaReader top = Open();
        using MediaReader bottom = Open();
        Read(top, 0, 1);
        Read(bottom, 5000, 1);
        _nextOpen = () => throw new IOException("unreadable");

        Read(top, 1, 1);

        Assert.That(_opened, Has.Count.EqualTo(1));
        Assert.That(_opened[0].Reads, Is.EqualTo(3), "the failed open must not drop the read");
    }

    [Test]
    public void AnExtraReaderThatResolvedDifferently_IsRejected()
    {
        using MediaReader top = Open();
        using MediaReader bottom = Open();
        Read(top, 0, 1);
        Read(bottom, 5000, 1);
        // For example, the proxy the first reader resolved was removed before this open.
        _nextOpen = () => new FakeReader(hasVideo: false);

        Read(top, 1, 1);

        Assert.That(_opened, Has.Count.EqualTo(2));
        Assert.That(_opened[1].IsDisposed, Is.True);
        Assert.That(_opened[1].Reads, Is.Zero);
        Assert.That(_opened[0].Reads, Is.EqualTo(3));
    }

    [Test]
    public async Task AJoinerOfAFailedOpen_OpensOnItsOwn()
    {
        using var gate = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<MediaReader> failing = Task.Run(() => _pool.Open(s_key, () =>
        {
            entered.SetResult();
            gate.Wait(TimeSpan.FromSeconds(15));
            throw new IOException("unreadable");
        }));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Task<MediaReader> joiner = Task.Run(() => Open());
        await Task.Delay(50);
        gate.Set();

        Assert.That(async () => await failing.WaitAsync(TimeSpan.FromSeconds(10)), Throws.InstanceOf<IOException>());
        using MediaReader joined = await joiner.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(joined.HasVideo, Is.True);
        Assert.That(_opened, Has.Count.EqualTo(1));
    }

    [Test]
    public void ADisposedHandle_ReadsNothing()
    {
        using MediaReader live = Open();
        MediaReader disposed = Open();
        disposed.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(disposed.ReadVideo(0, out _), Is.False);
            Assert.That(disposed.ReadAudio(0, 100, out _), Is.False);
            Assert.That(_opened[0].Reads, Is.Zero);
            Assert.That(_opened[0].IsDisposed, Is.False, "the live handle still reads it");
        });
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
    private sealed class FakeReader(bool hasVideo = true) : MediaReader
    {
        private long? _next;

        public int Seeks { get; private set; }

        public int Reads { get; private set; }

        // When set, the next read signals Entered and waits for Gate.
        public TaskCompletionSource? Entered { get; set; }

        public ManualResetEventSlim? Gate { get; set; }

        public override VideoStreamInfo VideoInfo { get; } = new("fake", 100_000L, new PixelSize(16, 16), new Rational(30, 1));

        public override AudioStreamInfo AudioInfo { get; } = new("fake", new Rational(3600, 1), 48_000, 2);

        public override bool HasVideo => hasVideo;

        public override bool HasAudio => true;

        public override bool ReadVideo(int frame, [NotNullWhen(true)] out Ref<Bitmap>? image)
        {
            if (Gate is { } gate)
            {
                Gate = null;
                Entered?.TrySetResult();
                gate.Wait(TimeSpan.FromSeconds(15));
            }
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
