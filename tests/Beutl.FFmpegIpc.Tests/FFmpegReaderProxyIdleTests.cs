using System.Collections.Concurrent;
using System.IO.Pipes;
using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegIpc.Transport;

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

    // Answers ReadVideo/ReadAudio/CloseReader like the worker, writing frames into shared memory it owns.
    private sealed class FakeWorker : IDisposable
    {
        private const int Width = 4;
        private const int Height = 4;
        private const int BytesPerPixel = 4;

        private readonly NamedPipeServerStream _server;
        private readonly CancellationTokenSource _cts = new();
        private readonly ConcurrentDictionary<int, SharedMemoryBuffer> _buffers = new();
        private readonly ConcurrentDictionary<int, bool> _closed = new();
        private readonly Task _loop;
        private int _nextReaderId;

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

        public OpenFileResponse OpenVideo(int frameRateNum = 30)
        {
            int id = Interlocked.Increment(ref _nextReaderId);
            long slotSize = Width * Height * BytesPerPixel + 64;
            string name = $"beutl-fake-video-{Guid.NewGuid():N}";
            _buffers[id] = SharedMemoryBuffer.Create(name, slotSize);
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
                VideoRingBufferSlotCount = 1,
                VideoRingBufferSlotSize = slotSize,
            };
        }

        public OpenFileResponse OpenAudio(int sampleRate)
        {
            int id = Interlocked.Increment(ref _nextReaderId);
            string name = $"beutl-fake-audio-{Guid.NewGuid():N}";
            _buffers[id] = SharedMemoryBuffer.Create(name, sampleRate * 8L + 64);
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

            foreach (SharedMemoryBuffer buffer in _buffers.Values)
                buffer.Dispose();
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

        private IpcMessage HandleReadVideo(IpcMessage request)
        {
            var payload = request.GetPayload<ReadVideoRequest>()!;
            if (!_buffers.TryGetValue(payload.ReaderId, out SharedMemoryBuffer? buffer))
                return IpcMessage.CreateError(request.Id, $"Unknown reader ID: {payload.ReaderId}");

            int length = Width * Height * BytesPerPixel;
            byte[] pixels = new byte[length];
            Array.Fill(pixels, PixelValue(payload.ReaderId, payload.Frame));
            buffer.Write(pixels);

            return IpcMessage.Create(request.Id, MessageType.ReadVideoResult, new ReadVideoResponse
            {
                Success = true,
                Width = Width,
                Height = Height,
                BytesPerPixel = BytesPerPixel,
                DataLength = length,
                SlotIndex = 0,
                SlotDataOffset = 0,
            });
        }

        private IpcMessage HandleReadAudio(IpcMessage request)
        {
            var payload = request.GetPayload<ReadAudioRequest>()!;
            if (!_buffers.ContainsKey(payload.ReaderId))
                return IpcMessage.CreateError(request.Id, $"Unknown reader ID: {payload.ReaderId}");

            // The buffer is zero-filled already, which is silence for stereo float samples.
            return IpcMessage.Create(request.Id, MessageType.ReadAudioResult, new ReadAudioResponse
            {
                Success = true,
                SampleRate = 8000,
                NumSamples = payload.Length,
                DataLength = payload.Length * 8,
            });
        }

        private IpcMessage HandleClose(IpcMessage request)
        {
            var payload = request.GetPayload<CloseReaderRequest>()!;
            if (_buffers.TryRemove(payload.ReaderId, out SharedMemoryBuffer? buffer))
                buffer.Dispose();
            _closed[payload.ReaderId] = true;
            return IpcMessage.CreateSimple(request.Id, MessageType.CloseReaderResult);
        }
    }
}
