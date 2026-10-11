using Beutl.FFmpegIpc;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegWorker.Handlers;
using Beutl.Logging;
using Beutl.Media.Decoding;

namespace Beutl.FFmpegWorker.Tests;

// The worker handles messages in parallel, so a CloseReader can retire a reader between the moment another operation
// looks the reader up and the moment it starts using it. That operation must answer as for an unknown reader instead
// of failing on the reader the close disposed.
[TestFixture, NonParallelizable]
public sealed class DecodingHandlerCloseRaceTests
{
    private static string VideoFixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "mf-unsupported.flv");

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance;
        try
        {
            FFmpegLoaderWorker.Initialize();
        }
        catch (FFmpegLibrariesNotFoundException)
        {
            Assert.Ignore("FFmpeg native libraries are not available.");
        }
        catch (DllNotFoundException)
        {
            Assert.Ignore("FFmpeg native library dependencies are not available.");
        }
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
            3, MessageType.UpdateDecoderSettings, new UpdateDecoderSettingsRequest { ThreadCount = 1 }));
        IpcMessage kept = handler.HandleReadVideo(IpcMessage.Create(
            4, MessageType.ReadVideo, new ReadVideoRequest { ReaderId = keptId, Frame = 0 }));

        Assert.Multiple(() =>
        {
            Assert.That(close.Response?.Type, Is.EqualTo(MessageType.CloseReaderResult), "the close must run after the lookup");
            Assert.That(response.Type, Is.EqualTo(MessageType.UpdateDecoderSettingsResult), response.Error);
            Assert.That(kept.GetPayload<ReadVideoResponse>()?.Success, Is.True, "the other reader must keep working");
        });
    }

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

    // 0.1 s of 8 kHz mono 16-bit silence.
    private static void WriteSilentPcmWav(string path)
    {
        const int sampleRate = 8000;
        const int dataSize = 1600;

        using var writer = new BinaryWriter(new FileStream(path, FileMode.Create, FileAccess.Write));
        writer.Write("RIFF"u8);
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)1); // mono
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
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
