using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegWorker.Handlers;
using Beutl.Media.Decoding;
using static Beutl.FFmpegIpc.Tests.TestWav;

namespace Beutl.FFmpegWorker.Tests;

// The worker handles messages in parallel, so a CloseReader can retire a reader between the moment another operation
// looks the reader up and the moment it starts using it. That operation must answer as for an unknown reader instead
// of failing on the reader the close disposed. A close that arrives while an operation uses the reader must wait for it.
[TestFixture, NonParallelizable]
public sealed class DecodingHandlerCloseRaceTests
{
    // Bounds every wait, so a close that never completes fails the test instead of hanging the run.
    private static readonly TimeSpan WaitLimit = TimeSpan.FromMinutes(1);

    // How long a close must stay blocked while a use is held. A close that does not wait finishes well within it.
    private static readonly TimeSpan StillBlockedWait = TimeSpan.FromSeconds(1);

    private static string VideoFixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "mf-unsupported.flv");

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        FFmpegTestHelpers.RequireFFmpeg();
    }

    [Test]
    public void ReadVideo_OfAReaderClosedAfterItsLookup_AnswersAsForAnUnknownReader()
    {
        using var handler = new DecodingHandler();
        int readerId = Open(handler, VideoFixturePath, MediaMode.Video);
        var close = CloseAfterLookup.Arm(handler, readerId);

        IpcMessage response = handler.HandleReadVideo(IpcMessage.Create(
            2, MessageType.ReadVideo, new ReadVideoRequest { ReaderId = readerId, Frame = 0 }));

        AssertAnsweredAsUnknown(response, readerId, close);
    }

    [Test]
    public void ReadAudio_OfAReaderClosedAfterItsLookup_AnswersAsForAnUnknownReader()
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-close-race-{Guid.NewGuid():N}.wav");
        WriteSilentPcmWav(path);
        try
        {
            using var handler = new DecodingHandler();
            int readerId = Open(handler, path, MediaMode.Audio);
            var close = CloseAfterLookup.Arm(handler, readerId);

            IpcMessage response = handler.HandleReadAudio(IpcMessage.Create(
                2, MessageType.ReadAudio, new ReadAudioRequest { ReaderId = readerId, Start = 0, Length = 400 }));

            AssertAnsweredAsUnknown(response, readerId, close);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void UpdateDecoderSettings_SkipsAReaderClosedAfterItsLookup()
    {
        using var handler = new DecodingHandler();
        int closedId = Open(handler, VideoFixturePath, MediaMode.Video);
        int keptId = Open(handler, VideoFixturePath, MediaMode.Video);
        var close = CloseAfterLookup.Arm(handler, closedId);

        IpcMessage response = handler.HandleUpdateDecoderSettings(IpcMessage.Create(
            3, MessageType.UpdateDecoderSettings, new UpdateDecoderSettingsRequest { ForceSrgbGamma = true }));
        IpcMessage kept = handler.HandleReadVideo(IpcMessage.Create(
            4, MessageType.ReadVideo, new ReadVideoRequest { ReaderId = keptId, Frame = 0 }));

        Assert.Multiple(() =>
        {
            Assert.That(close.Response?.Type, Is.EqualTo(MessageType.CloseReaderResult), "the close must run after the lookup");
            Assert.That(response.Type, Is.EqualTo(MessageType.UpdateDecoderSettingsResult), response.Error);
            Assert.That(kept.GetPayload<ReadVideoResponse>()?.Success, Is.True, "the other reader must keep working");
        });
    }

    [Test]
    public void Close_WaitsForAReadInProgress_AndCompletesOnceTheReadEnds()
    {
        using var handler = new DecodingHandler();
        int readerId = Open(handler, VideoFixturePath, MediaMode.Video);
        using var release = new ManualResetEventSlim();
        try
        {
            Task<IpcMessage> read = StartHeldRead(handler, readerId, release);
            Task<IpcMessage> close = Task.Run(() => handler.HandleClose(CloseMessage(readerId)));
            Assert.That(close.Wait(StillBlockedWait), Is.False, "the close must wait while the read holds its use");

            release.Set();
            Assert.That(read.Wait(WaitLimit), Is.True, "the read must finish once released");
            Assert.That(close.Wait(WaitLimit), Is.True, "the close must complete once the read has ended");
            Assert.Multiple(() =>
            {
                Assert.That(read.Result.GetPayload<ReadVideoResponse>()?.Success, Is.True, "the read must finish normally");
                Assert.That(close.Result.Type, Is.EqualTo(MessageType.CloseReaderResult));
            });
        }
        finally
        {
            release.Set();
        }
    }

    [Test]
    public void Close_CompletesOnceAReadInProgressFails()
    {
        using var handler = new DecodingHandler();
        int readerId = Open(handler, VideoFixturePath, MediaMode.Video);
        using var release = new ManualResetEventSlim();
        try
        {
            Task<IpcMessage> read = StartHeldRead(
                handler, readerId, release, new InvalidOperationException("The read failed."));
            Task<IpcMessage> close = Task.Run(() => handler.HandleClose(CloseMessage(readerId)));
            Assert.That(close.Wait(StillBlockedWait), Is.False, "the close must wait while the read holds its use");

            release.Set();
            var failure = Assert.Throws<AggregateException>(() => read.Wait(WaitLimit), "the read must fail once released");
            Assert.Multiple(() =>
            {
                Assert.That(failure!.InnerException, Is.TypeOf<InvalidOperationException>());
                Assert.That(close.Wait(WaitLimit), Is.True, "the close must complete once the failed read has ended its use");
            });
        }
        finally
        {
            release.Set();
        }
    }

    // Starts a ReadVideo on another thread and returns once it holds its use of the reader. The read stays there until
    // released, then reads the frame, or throws the given failure.
    private static Task<IpcMessage> StartHeldRead(
        DecodingHandler handler, int readerId, ManualResetEventSlim release, Exception? failure = null)
    {
        var inUse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.ReadVideoInUseForTest = _ =>
        {
            inUse.TrySetResult();
            release.Wait(WaitLimit);
            if (failure is not null)
                throw failure;
        };

        Task<IpcMessage> read = Task.Run(() => handler.HandleReadVideo(IpcMessage.Create(
            2, MessageType.ReadVideo, new ReadVideoRequest { ReaderId = readerId, Frame = 0 })));
        Assert.That(inUse.Task.Wait(WaitLimit), Is.True, "the read must begin its use of the reader");
        return read;
    }

    private static IpcMessage CloseMessage(int readerId)
        => IpcMessage.Create(5, MessageType.CloseReader, new CloseReaderRequest { ReaderId = readerId });

    private static void AssertAnsweredAsUnknown(IpcMessage response, int readerId, CloseAfterLookup close)
    {
        Assert.Multiple(() =>
        {
            Assert.That(close.Response?.Type, Is.EqualTo(MessageType.CloseReaderResult), "the close must run after the lookup");
            Assert.That(response.Type, Is.EqualTo(MessageType.Error));
            Assert.That(response.Error, Is.EqualTo($"Unknown reader ID: {readerId}"));
        });
    }

    private static int Open(DecodingHandler handler, string path, MediaMode mode)
    {
        IpcMessage response = handler.HandleOpen(IpcMessage.Create(
            1, MessageType.OpenFile, new OpenFileRequest { FilePath = path, StreamsToLoad = (int)mode }));
        return response.GetPayload<OpenFileResponse>()!.ReaderId;
    }

    // Closes the reader on the test thread right after the next operation looked it up, as a CloseReader that the
    // worker handles in parallel can.
    private sealed class CloseAfterLookup
    {
        public IpcMessage? Response { get; private set; }

        public static CloseAfterLookup Arm(DecodingHandler handler, int readerId)
        {
            var close = new CloseAfterLookup();
            handler.ReaderLookedUpForTest = id =>
            {
                if (id != readerId)
                    return;

                handler.ReaderLookedUpForTest = null;
                close.Response = handler.HandleClose(IpcMessage.Create(
                    5, MessageType.CloseReader, new CloseReaderRequest { ReaderId = readerId }));
            };
            return close;
        }
    }
}
