using System.Collections.Concurrent;
using System.IO.Pipes;
using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegIpc.Transport;
using Beutl.Media.Decoding;

namespace Beutl.FFmpegIpc.Tests;

/// <summary>
/// Suspension and reopening of <see cref="FFmpegReaderProxy"/> against an in-process fake worker, so the
/// lifecycle is exercised without FFmpeg natives (the process-level contract tests skip without them).
/// </summary>
[TestFixture, NonParallelizable]
public class FFmpegReaderProxyIdleTests
{
    private FakeWorker _worker = null!;
    private FFmpegReaderIdleTracker _tracker = null!;

    [SetUp]
    public void SetUp()
    {
        _worker = new FakeWorker();
        _tracker = new FFmpegReaderIdleTracker(new FFmpegReaderIdlePolicy.Limits(0, 0, 0), sweepInterval: null);
    }

    [TearDown]
    public void TearDown()
    {
        _tracker.Dispose();
        _worker.Dispose();
    }

    [Test]
    public void ReadVideo_ReturnsTheFrameTheWorkerWrote()
    {
        using var reader = CreateVideoProxy();

        Assert.That(ReadFirstByte(reader, 3), Is.EqualTo(FakeWorker.PixelValue(reader.ReaderId, 3)));
    }

    [Test]
    public void Sweep_SuspendsTheReader_AndTheNextReadReopensIt()
    {
        int reopenCount = 0;
        using var reader = CreateVideoProxy(() =>
        {
            reopenCount++;
            return (_worker.Connection, _worker.OpenVideo());
        });
        int firstId = reader.ReaderId;
        ReadFirstByte(reader, 0);

        _tracker.Sweep(Environment.TickCount64 + 1);

        Assert.Multiple(() =>
        {
            Assert.That(reader.IsSuspended, Is.True);
            Assert.That(_tracker.TrackedCount, Is.Zero);
        });
        _worker.WaitForClose(firstId);

        Assert.Multiple(() =>
        {
            Assert.That(ReadFirstByte(reader, 2), Is.EqualTo(FakeWorker.PixelValue(reader.ReaderId, 2)));
            Assert.That(reader.ReaderId, Is.Not.EqualTo(firstId));
            Assert.That(reader.IsSuspended, Is.False);
            Assert.That(reopenCount, Is.EqualTo(1));
            Assert.That(_tracker.TrackedCount, Is.EqualTo(1), "a resumed reader is tracked again");
        });
    }

    // Reopening starts a new worker when the old one exited, which comes with a new connection.
    [Test]
    public void Resume_ReadsOverTheConnectionReturnedByReopen()
    {
        using var restartedWorker = new FakeWorker(firstReaderId: 9);
        using var reader = CreateVideoProxy(() => (restartedWorker.Connection, restartedWorker.OpenVideo()));
        Suspend(reader);

        // The original worker does not know reader 9, so a read over the stale connection would fail.
        Assert.Multiple(() =>
        {
            Assert.That(ReadFirstByte(reader, 1), Is.EqualTo(FakeWorker.PixelValue(9, 1)));
            Assert.That(reader.ReaderId, Is.EqualTo(9));
        });
    }

    [Test]
    public void FailedReopen_ReturnsFalse_AndTheNextReadRetries()
    {
        int attempts = 0;
        using var reader = CreateVideoProxy(() =>
        {
            if (++attempts == 1)
                throw new FFmpegWorkerException("The file is missing.", null, null);
            return (_worker.Connection, _worker.OpenVideo());
        });
        Suspend(reader);

        Assert.Multiple(() =>
        {
            Assert.That(reader.ReadVideo(0, out var image), Is.False);
            Assert.That(image, Is.Null);
            Assert.That(reader.IsSuspended, Is.True);
        });

        Assert.That(ReadFirstByte(reader, 1), Is.EqualTo(FakeWorker.PixelValue(reader.ReaderId, 1)));
        Assert.That(attempts, Is.EqualTo(2));
    }

    [Test]
    public void ReopenWithOtherStreams_ClosesTheNewReader_AndStopsReturningMedia()
    {
        int attempts = 0;
        int reopenedId = 0;
        using var reader = CreateVideoProxy(() =>
        {
            attempts++;
            OpenFileResponse response = _worker.OpenVideo(frameRateNum: 60);
            reopenedId = response.ReaderId;
            return (_worker.Connection, response);
        });
        int suspendedId = reader.ReaderId;
        Suspend(reader);

        Assert.That(reader.ReadVideo(0, out _), Is.False);
        _worker.WaitForClose(reopenedId);

        Assert.Multiple(() =>
        {
            Assert.That(reader.ReadVideo(0, out _), Is.False);
            Assert.That(attempts, Is.EqualTo(1), "a replaced file is not reopened on every read");
            Assert.That(reader.ReaderId, Is.EqualTo(suspendedId));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReadAfterDispose_ReturnsFalse(bool suspendFirst)
    {
        int reopenCount = 0;
        var reader = CreateVideoProxy(() =>
        {
            reopenCount++;
            return (_worker.Connection, _worker.OpenVideo());
        });
        int readerId = reader.ReaderId;
        if (suspendFirst)
            Suspend(reader);

        reader.Dispose();
        _worker.WaitForClose(readerId);

        Assert.Multiple(() =>
        {
            Assert.That(reader.ReadVideo(0, out var image), Is.False);
            Assert.That(image, Is.Null);
            Assert.That(reopenCount, Is.Zero);
            Assert.That(_tracker.TrackedCount, Is.Zero);
        });
    }

    [Test]
    public void TrySuspend_Declines_WhenTheReaderWasReadAfterTheObservedAccess()
    {
        using var reader = CreateVideoProxy();
        var suspendable = (IIdleSuspendableReader)reader;
        long observed = suspendable.LastAccessTicks;
        Thread.Sleep(50);
        ReadFirstByte(reader, 0);

        Assert.Multiple(() =>
        {
            Assert.That(suspendable.TrySuspend(observed), Is.False);
            Assert.That(reader.IsSuspended, Is.False);
        });
    }

    [Test]
    public void AudioOnlyReader_IsNotTracked_AndReadsAudio()
    {
        OpenFileResponse response = _worker.OpenAudio(sampleRate: 8000);
        using var reader = new FFmpegReaderProxy(
            _worker.Connection, response.ReaderId, response,
            () => (_worker.Connection, _worker.OpenAudio(sampleRate: 8000)), _tracker);

        Assert.That(_tracker.TrackedCount, Is.Zero);
        Assert.That(reader.ReadAudio(0, 100, out var single), Is.True);
        using (single)
            Assert.That(single!.Value.NumSamples, Is.EqualTo(100));

        // Longer than one second, so the proxy splits the request into chunks.
        Assert.That(reader.ReadAudio(0, 9000, out var chunked), Is.True);
        using (chunked)
            Assert.That(chunked!.Value.NumSamples, Is.EqualTo(9000));
    }

    // The sweep runs on a timer thread; it must never close the worker reader under a read in progress.
    [Test]
    public void TrySuspend_Declines_WhileAReadIsInProgress()
    {
        using var reader = CreateVideoProxy();
        var suspendable = (IIdleSuspendableReader)reader;
        _worker.ReleaseReads.Reset();
        Task<byte> read = Task.Run(() => ReadFirstByte(reader, 4));
        try
        {
            Assert.That(_worker.ReadVideoReceived.Wait(TimeSpan.FromSeconds(5)), Is.True);

            Assert.That(suspendable.TrySuspend(suspendable.LastAccessTicks), Is.False);
        }
        finally
        {
            _worker.ReleaseReads.Set();
        }

        Assert.Multiple(() =>
        {
            Assert.That(read.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(read.Result, Is.EqualTo(FakeWorker.PixelValue(reader.ReaderId, 4)));
            Assert.That(reader.IsSuspended, Is.False);
        });
    }

    [Test]
    public void TrySuspend_Declines_ForSuspendedDisposedOrNonReopenableReaders()
    {
        using var suspended = CreateVideoProxy();
        Suspend(suspended);
        var disposed = CreateVideoProxy();
        disposed.Dispose();
        OpenFileResponse response = _worker.OpenVideo();
        using var withoutReopen = new FFmpegReaderProxy(_worker.Connection, response.ReaderId, response);

        Assert.Multiple(() =>
        {
            Assert.That(TrySuspend(suspended), Is.False, "already suspended");
            Assert.That(TrySuspend(disposed), Is.False, "disposed");
            Assert.That(TrySuspend(withoutReopen), Is.False, "cannot be reopened");
            Assert.That(withoutReopen.IsSuspended, Is.False);
        });
    }

    // Audio-only readers are not swept, but suspending one directly must still reopen it correctly.
    [Test]
    public void AudioOnlyReader_ReopensAfterSuspension_AndReturnsFalseOnceDisposed()
    {
        int reopenCount = 0;
        OpenFileResponse response = _worker.OpenAudio(sampleRate: 8000);
        var reader = new FFmpegReaderProxy(
            _worker.Connection, response.ReaderId, response,
            () =>
            {
                reopenCount++;
                return (_worker.Connection, _worker.OpenAudio(sampleRate: 8000));
            },
            _tracker);
        int firstId = reader.ReaderId;
        Assert.That(ReadSamples(reader, 100), Is.EqualTo(100));

        Suspend(reader);
        Assert.Multiple(() =>
        {
            Assert.That(ReadSamples(reader, 100), Is.EqualTo(100));
            Assert.That(reader.ReaderId, Is.Not.EqualTo(firstId));
            Assert.That(reopenCount, Is.EqualTo(1));
            Assert.That(_tracker.TrackedCount, Is.Zero, "audio-only readers are not tracked");
        });

        reader.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(reader.ReadAudio(0, 100, out var sound), Is.False);
            Assert.That(sound, Is.Null);
        });
    }

    [Test]
    public void ReadVideo_FollowsTheSharedMemoryTheWorkerRecreated()
    {
        using var reader = CreateVideoProxy();
        _worker.RecreateVideoBufferOnNextRead(reader.ReaderId);

        Assert.That(ReadFirstByte(reader, 1), Is.EqualTo(FakeWorker.PixelValue(reader.ReaderId, 1)));
        // Later reads report no name, so the proxy must keep the recreated mapping.
        Assert.That(ReadFirstByte(reader, 2), Is.EqualTo(FakeWorker.PixelValue(reader.ReaderId, 2)));
    }

    [Test]
    public void ReadAudio_FollowsTheSharedMemoryTheWorkerRecreated()
    {
        OpenFileResponse response = _worker.OpenAudio(sampleRate: 8000, capacitySamples: 100);
        using var reader = new FFmpegReaderProxy(_worker.Connection, response.ReaderId, response);

        Assert.That(ReadSamples(reader, 1000), Is.EqualTo(1000));
        Assert.That(ReadSamples(reader, 500), Is.EqualTo(500));
    }

    [Test]
    public void DecoderInfo_Open_SendsTheDecodingSettings_AndTracksTheReader()
    {
        var settings = new FFmpegDecodingSettings
        {
            ThreadCount = 3,
            Acceleration = FFmpegDecodingSettings.AccelerationOptions.D3D11VA,
            ForceSrgbGamma = false,
        };
        var decoderInfo = new FFmpegDecoderInfo(settings, () => _worker.Connection, _tracker);

        using var reader = decoderInfo.Open("clip.mp4", new MediaOptions(MediaMode.Video)) as FFmpegReaderProxy;

        Assert.That(reader, Is.Not.Null);
        Assert.That(_worker.OpenRequests.TryDequeue(out OpenFileRequest? request), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(request!.FilePath, Is.EqualTo("clip.mp4"));
            Assert.That(request.StreamsToLoad, Is.EqualTo((int)MediaMode.Video));
            Assert.That(request.ThreadCount, Is.EqualTo(3));
            Assert.That(request.Acceleration, Is.EqualTo((int)FFmpegDecodingSettings.AccelerationOptions.D3D11VA));
            Assert.That(request.ForceSrgbGamma, Is.False);
            Assert.That(_tracker.TrackedCount, Is.EqualTo(1));
            Assert.That(ReadFirstByte(reader!, 0), Is.EqualTo(FakeWorker.PixelValue(reader!.ReaderId, 0)));
        });
    }

    [Test]
    public void DecoderInfo_Reopen_SendsTheCurrentDecodingSettings()
    {
        var settings = new FFmpegDecodingSettings { ThreadCount = 2 };
        var decoderInfo = new FFmpegDecoderInfo(settings, () => _worker.Connection, _tracker);
        using var reader = (FFmpegReaderProxy)decoderInfo.Open("clip.mp4", new MediaOptions(MediaMode.Video))!;
        Assert.That(_worker.OpenRequests.TryDequeue(out _), Is.True);

        settings.ThreadCount = 6;
        Suspend(reader);
        ReadFirstByte(reader, 0);

        Assert.That(_worker.OpenRequests.TryDequeue(out OpenFileRequest? reopenRequest), Is.True);
        Assert.That(reopenRequest!.ThreadCount, Is.EqualTo(6));
    }

    // The idle budget counts the ring buffer the worker allocated: HDR slots take 8 bytes per pixel, and
    // the worker grows the buffer for larger frames without ever shrinking it.
    [Test]
    public void MemoryBytes_IsTheRingBufferTheWorkerAllocated()
    {
        OpenFileResponse hdr = _worker.OpenVideo(slotCount: 4, bytesPerPixel: 8);
        using var hdrReader = new FFmpegReaderProxy(_worker.Connection, hdr.ReaderId, hdr);

        Assert.That(((IIdleSuspendableReader)hdrReader).MemoryBytes, Is.EqualTo(4 * (4 * 4 * 8 + 64)));
    }

    [Test]
    public void MemoryBytes_FollowsTheRingBufferAcrossResizeAndReopen()
    {
        using var reader = CreateVideoProxy();
        var suspendable = (IIdleSuspendableReader)reader;
        long opened = suspendable.MemoryBytes;
        Assert.That(opened, Is.EqualTo(4 * 4 * 4 + 64));

        _worker.ResizeVideoOnNextRead(reader.ReaderId, 8, 8);
        Assert.That(reader.ReadVideo(0, out var image), Is.True);
        using (image)
            Assert.That(image!.Value.Width, Is.EqualTo(8));
        Assert.That(suspendable.MemoryBytes, Is.EqualTo(_worker.SlotSizeOf(reader.ReaderId)).And.GreaterThan(opened));

        Suspend(reader);
        ReadFirstByte(reader, 0);
        Assert.That(suspendable.MemoryBytes, Is.EqualTo(opened), "a reopened reader starts with a fresh ring buffer");
    }

    // A close stuck behind a long decode must not lead to a second decoder next to the old one.
    [Test]
    public void Resume_StaysSuspended_WhenTheCloseOutlastsTheTimeout()
    {
        int reopenCount = 0;
        OpenFileResponse response = _worker.OpenVideo();
        using var reader = new FFmpegReaderProxy(
            _worker.Connection, response.ReaderId, response,
            () =>
            {
                reopenCount++;
                return (_worker.Connection, _worker.OpenVideo());
            },
            _tracker)
        {
            CloseTimeout = TimeSpan.FromMilliseconds(100),
        };

        _worker.ReleaseCloses.Reset();
        try
        {
            Assert.That(TrySuspend(reader), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(reader.ReadVideo(0, out var image), Is.False);
                Assert.That(image, Is.Null);
                Assert.That(reader.IsSuspended, Is.True);
                Assert.That(reopenCount, Is.Zero);
            });
        }
        finally
        {
            _worker.ReleaseCloses.Set();
        }

        _worker.WaitForClose(response.ReaderId);
        Assert.That(ReadFirstByte(reader, 0), Is.EqualTo(FakeWorker.PixelValue(reader.ReaderId, 0)));
        Assert.That(reopenCount, Is.EqualTo(1));
    }

    [Test]
    public void Resume_WaitsUntilTheWorkerClosedTheSuspendedReader()
    {
        bool? closedWhenReopened = null;
        int suspendedId = 0;
        using var reader = CreateVideoProxy(() =>
        {
            closedWhenReopened = _worker.IsClosed(suspendedId);
            return (_worker.Connection, _worker.OpenVideo());
        });
        suspendedId = reader.ReaderId;

        _worker.ReleaseCloses.Reset();
        Task<byte>? read = null;
        try
        {
            Assert.That(TrySuspend(reader), Is.True);
            Assert.That(_worker.CloseReceived.Wait(TimeSpan.FromSeconds(5)), Is.True);

            read = Task.Run(() => ReadFirstByte(reader, 0));
            Thread.Sleep(100);
            Assert.Multiple(() =>
            {
                Assert.That(read.IsCompleted, Is.False, "the read must wait for the close");
                Assert.That(closedWhenReopened, Is.Null, "nothing may be reopened before the close completes");
            });
        }
        finally
        {
            _worker.ReleaseCloses.Set();
        }

        Assert.Multiple(() =>
        {
            Assert.That(read!.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(closedWhenReopened, Is.True);
        });
    }

    private static bool TrySuspend(FFmpegReaderProxy reader)
    {
        var suspendable = (IIdleSuspendableReader)reader;
        return suspendable.TrySuspend(suspendable.LastAccessTicks);
    }

    private static int ReadSamples(FFmpegReaderProxy reader, int length)
    {
        Assert.That(reader.ReadAudio(0, length, out var sound), Is.True);
        using (sound)
            return sound!.Value.NumSamples;
    }

    private FFmpegReaderProxy CreateVideoProxy(
        Func<(IpcConnection Connection, OpenFileResponse Response)>? reopen = null)
    {
        OpenFileResponse response = _worker.OpenVideo();
        return new FFmpegReaderProxy(
            _worker.Connection, response.ReaderId, response,
            reopen ?? (() => (_worker.Connection, _worker.OpenVideo())), _tracker);
    }

    private void Suspend(FFmpegReaderProxy reader)
    {
        var suspendable = (IIdleSuspendableReader)reader;
        int readerId = reader.ReaderId;
        Assert.That(suspendable.TrySuspend(suspendable.LastAccessTicks), Is.True);
        _worker.WaitForClose(readerId);
    }

    private static byte ReadFirstByte(FFmpegReaderProxy reader, int frame)
    {
        Assert.That(reader.ReadVideo(frame, out var image), Is.True);
        using (image)
            return image!.Value.GetPixelSpan()[0];
    }

    // Answers OpenFile/ReadVideo/ReadAudio/CloseReader like the worker, writing media into shared memory it owns.
    private sealed class FakeWorker : IDisposable
    {
        private const int Width = 4;
        private const int Height = 4;
        private const int BytesPerPixel = 4;

        private readonly NamedPipeServerStream _server;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<int, SharedMemoryBuffer> _buffers = new();
        private readonly ConcurrentDictionary<int, bool> _closed = new();
        private readonly ConcurrentDictionary<int, bool> _recreateVideoBuffer = new();
        private readonly ConcurrentDictionary<int, (int Width, int Height)> _videoSizes = new();
        private readonly ConcurrentBag<SharedMemoryBuffer> _retiredBuffers = [];
        private readonly Task _loop;
        private int _nextReaderId;

        public ConcurrentQueue<OpenFileRequest> OpenRequests { get; } = new();

        // Signalled when a ReadVideo arrives; while ReleaseReads is reset, the worker holds its response.
        public ManualResetEventSlim ReadVideoReceived { get; } = new(false);

        public ManualResetEventSlim ReleaseReads { get; } = new(true);

        // Signalled when a CloseReader arrives; while ReleaseCloses is reset, the reader stays open.
        public ManualResetEventSlim CloseReceived { get; } = new(false);

        public ManualResetEventSlim ReleaseCloses { get; } = new(true);

        public FakeWorker(int firstReaderId = 1)
        {
            _nextReaderId = firstReaderId - 1;
            string name = "beutl-fake-worker-" + Guid.NewGuid().ToString("N")[..8];
            _server = new NamedPipeServerStream(
                name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            Task connect = _server.WaitForConnectionAsync();
            client.Connect(TimeSpan.FromSeconds(5));
            connect.GetAwaiter().GetResult();

            Connection = new IpcConnection(client);
            Connection.StartMultiplexedReceive();
            _loop = Task.Run(RunAsync);
        }

        public IpcConnection Connection { get; }

        public static byte PixelValue(int readerId, int frame) => (byte)(readerId * 16 + frame);

        public OpenFileResponse OpenVideo(int frameRateNum = 30, int slotCount = 1, int bytesPerPixel = BytesPerPixel)
        {
            int id = Interlocked.Increment(ref _nextReaderId);
            long slotSize = Width * Height * bytesPerPixel + 64;
            string name = $"beutl-fake-video-{Guid.NewGuid():N}";
            _buffers[id] = SharedMemoryBuffer.Create(name, slotSize * slotCount);
            return new OpenFileResponse
            {
                ReaderId = id,
                HasVideo = true,
                VideoCodecName = "fake",
                VideoNumFrames = 30,
                VideoWidth = Width,
                VideoHeight = Height,
                FrameRateNum = frameRateNum,
                FrameRateDen = 1,
                DurationNum = 1,
                DurationDen = 1,
                VideoSharedMemoryName = name,
                VideoRingBufferSlotCount = slotCount,
                VideoRingBufferSlotSize = slotSize,
            };
        }

        public OpenFileResponse OpenAudio(int sampleRate, int capacitySamples = -1)
        {
            int id = Interlocked.Increment(ref _nextReaderId);
            string name = $"beutl-fake-audio-{Guid.NewGuid():N}";
            _buffers[id] = SharedMemoryBuffer.Create(name, (capacitySamples < 0 ? sampleRate : capacitySamples) * 8L + 64);
            return new OpenFileResponse
            {
                ReaderId = id,
                HasAudio = true,
                AudioCodecName = "fake",
                AudioSampleRate = sampleRate,
                AudioNumChannels = 2,
                AudioDurationNum = 10,
                AudioDurationDen = 1,
                AudioSharedMemoryName = name,
            };
        }

        // Like the worker growing a ring buffer, the next read answers from a new mapping with a new name.
        public void RecreateVideoBufferOnNextRead(int readerId) => _recreateVideoBuffer[readerId] = true;

        public void ResizeVideoOnNextRead(int readerId, int width, int height)
        {
            _videoSizes[readerId] = (width, height);
            _recreateVideoBuffer[readerId] = true;
        }

        public bool IsClosed(int readerId) => _closed.ContainsKey(readerId);

        public long SlotSizeOf(int readerId) => _buffers[readerId].Capacity;

        public void WaitForClose(int readerId)
        {
            Assert.That(() => _closed.ContainsKey(readerId), Is.True.After(5000, 10),
                $"the worker never received CloseReader for reader {readerId}");
        }

        public void Dispose()
        {
            _cts.Cancel();
            Connection.Dispose();
            _server.Dispose();
            try
            {
                _loop.Wait(TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
            }

            foreach (SharedMemoryBuffer buffer in _buffers.Values.Concat(_retiredBuffers))
                buffer.Dispose();
            ReadVideoReceived.Dispose();
            ReleaseReads.Dispose();
            CloseReceived.Dispose();
            ReleaseCloses.Dispose();
        }

        private async Task RunAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                IpcMessage? request;
                try
                {
                    request = await MessageSerializer.ReadMessageAsync(_server, _cts.Token);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
                {
                    return;
                }

                if (request == null)
                    return;

                IpcMessage response = request.Type switch
                {
                    MessageType.OpenFile => HandleOpen(request),
                    MessageType.ReadVideo => HandleReadVideo(request),
                    MessageType.ReadAudio => HandleReadAudio(request),
                    MessageType.CloseReader => HandleClose(request),
                    _ => IpcMessage.CreateError(request.Id, $"Unsupported message: {request.Type}"),
                };

                try
                {
                    await MessageSerializer.WriteMessageAsync(_server, response, _cts.Token);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
                {
                    return;
                }
            }
        }

        private IpcMessage HandleOpen(IpcMessage request)
        {
            OpenRequests.Enqueue(request.GetPayload<OpenFileRequest>()!);
            return IpcMessage.Create(request.Id, MessageType.OpenFileResult, OpenVideo());
        }

        private IpcMessage HandleReadVideo(IpcMessage request)
        {
            var payload = request.GetPayload<ReadVideoRequest>()!;
            ReadVideoReceived.Set();
            ReleaseReads.Wait(TimeSpan.FromSeconds(10));
            if (!_buffers.TryGetValue(payload.ReaderId, out SharedMemoryBuffer? buffer))
                return IpcMessage.CreateError(request.Id, $"Unknown reader ID: {payload.ReaderId}");

            (int width, int height) = _videoSizes.GetValueOrDefault(payload.ReaderId, (Width, Height));
            int length = width * height * BytesPerPixel;
            string? newName = null;
            long? newSlotSize = null;
            if (_recreateVideoBuffer.TryRemove(payload.ReaderId, out _))
            {
                newName = $"beutl-fake-video-{Guid.NewGuid():N}";
                newSlotSize = Math.Max(buffer.Capacity * 2, length + 64L);
                _retiredBuffers.Add(buffer);
                buffer = _buffers[payload.ReaderId] = SharedMemoryBuffer.Create(newName, newSlotSize.Value);
            }

            byte[] pixels = new byte[length];
            Array.Fill(pixels, PixelValue(payload.ReaderId, payload.Frame));
            buffer.Write(pixels);

            return IpcMessage.Create(request.Id, MessageType.ReadVideoResult, new ReadVideoResponse
            {
                Success = true,
                Width = width,
                Height = height,
                BytesPerPixel = BytesPerPixel,
                DataLength = length,
                SlotIndex = 0,
                SlotDataOffset = 0,
                SharedMemoryName = newName,
                RingBufferSlotSize = newSlotSize,
                RingBufferSlotCount = newName != null ? 1 : null,
            });
        }

        private IpcMessage HandleReadAudio(IpcMessage request)
        {
            var payload = request.GetPayload<ReadAudioRequest>()!;
            if (!_buffers.TryGetValue(payload.ReaderId, out SharedMemoryBuffer? buffer))
                return IpcMessage.CreateError(request.Id, $"Unknown reader ID: {payload.ReaderId}");

            // Like the worker, grow the buffer under a new name when the request does not fit.
            string? newName = null;
            long required = payload.Length * 8L + 64;
            if (buffer.Capacity < required)
            {
                newName = $"beutl-fake-audio-{Guid.NewGuid():N}";
                _retiredBuffers.Add(buffer);
                _buffers[payload.ReaderId] = SharedMemoryBuffer.Create(newName, required);
            }

            // Fresh buffers are zero-filled, which is silence for stereo float samples.
            return IpcMessage.Create(request.Id, MessageType.ReadAudioResult, new ReadAudioResponse
            {
                Success = true,
                SampleRate = 8000,
                NumSamples = payload.Length,
                DataLength = payload.Length * 8,
                SharedMemoryName = newName,
            });
        }

        private IpcMessage HandleClose(IpcMessage request)
        {
            var payload = request.GetPayload<CloseReaderRequest>()!;
            CloseReceived.Set();
            ReleaseCloses.Wait(TimeSpan.FromSeconds(10));
            if (_buffers.TryRemove(payload.ReaderId, out SharedMemoryBuffer? buffer))
                buffer.Dispose();
            _closed[payload.ReaderId] = true;
            return IpcMessage.CreateSimple(request.Id, MessageType.CloseReaderResult);
        }
    }
}
