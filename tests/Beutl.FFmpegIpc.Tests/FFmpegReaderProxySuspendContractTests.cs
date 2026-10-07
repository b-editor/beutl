using Beutl.Extensions.FFmpeg;
using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.Media.Decoding;

namespace Beutl.FFmpegIpc.Tests;

/// <summary>
/// Process-level contract for idle reader suspension: a suspended <see cref="FFmpegReaderProxy"/> must
/// release its reader in the shared worker process and transparently reopen on the next read, decoding
/// the same pixels as before.
/// </summary>
[TestFixture, NonParallelizable]
public class FFmpegReaderProxySuspendContractTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "mf-unsupported.flv");

    [SetUp]
    public void SetUp()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "Beutl.FFmpegWorker.dll"))
            && !File.Exists(Path.Combine(AppContext.BaseDirectory, "FFmpegWorker", "Beutl.FFmpegWorker.dll")))
        {
            Assert.Ignore("FFmpeg worker binary not present in the test output; skipping.");
        }

        try
        {
            FFmpegWorkerProcess.DecodingInstance.EnsureStarted();
        }
        catch (FFmpegLibrariesNotFoundException ex)
        {
            Assert.Ignore($"FFmpeg natives unavailable ({ex.Message}); skipping.");
        }
    }

    [Test]
    public void SuspendedReader_ClosesWorkerReader_AndReopensOnNextRead()
    {
        using var reader = OpenFixture();
        byte[] before = ReadFramePixels(reader, 0);
        int suspendedReaderId = reader.ReaderId;

        var suspendable = (IIdleSuspendableReader)reader;
        Assert.That(suspendable.TrySuspend(suspendable.LastAccessTicks), Is.True);
        Assert.That(reader.IsSuspended, Is.True);

        AssertWorkerReaderClosed(suspendedReaderId);

        byte[] after = ReadFramePixels(reader, 0);
        Assert.Multiple(() =>
        {
            Assert.That(reader.IsSuspended, Is.False);
            Assert.That(reader.ReaderId, Is.Not.EqualTo(suspendedReaderId));
            Assert.That(after, Is.EqualTo(before), "the reopened reader must decode the same frame");
        });
    }

    [Test]
    public void TrySuspend_DeclinesWhenReaderWasUsedAfterTheObservedAccess()
    {
        using var reader = OpenFixture();
        var suspendable = (IIdleSuspendableReader)reader;
        long observed = suspendable.LastAccessTicks;
        Thread.Sleep(50);
        ReadFramePixels(reader, 0);

        Assert.Multiple(() =>
        {
            Assert.That(suspendable.TrySuspend(observed), Is.False);
            Assert.That(reader.IsSuspended, Is.False);
        });
    }

    [Test]
    public void ReadAfterDisposeWhileSuspended_DoesNotReopen()
    {
        var reader = OpenFixture();
        var suspendable = (IIdleSuspendableReader)reader;
        Assert.That(suspendable.TrySuspend(suspendable.LastAccessTicks), Is.True);

        reader.Dispose();

        Assert.That(() => reader.ReadVideo(0, out _), Throws.TypeOf<ObjectDisposedException>());
    }

    private static FFmpegReaderProxy OpenFixture()
    {
        var decoderInfo = new FFmpegDecoderInfo(new FFmpegDecodingSettings());
        MediaReader? reader = decoderInfo.Open(FixturePath, new MediaOptions(MediaMode.Video));
        Assert.That(reader, Is.TypeOf<FFmpegReaderProxy>());
        return (FFmpegReaderProxy)reader!;
    }

    private static byte[] ReadFramePixels(MediaReader reader, int frame)
    {
        Assert.That(reader.ReadVideo(frame, out var image), Is.True);
        using (image)
        {
            return image!.Value.GetPixelSpan().ToArray();
        }
    }

    // CloseReader is sent fire-and-forget, so wait for the worker to forget the ID.
    private static void AssertWorkerReaderClosed(int readerId)
    {
        var connection = FFmpegWorkerProcess.DecodingInstance.EnsureStarted();
        var request = new ReadVideoRequest { ReaderId = readerId, Frame = 0 };
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            try
            {
                connection.RequestAsync<ReadVideoRequest, ReadVideoResponse>(
                    MessageType.ReadVideo, MessageType.ReadVideoResult, request).AsTask().GetAwaiter().GetResult();
            }
            catch (FFmpegWorkerException ex) when (ex.Message.Contains("Unknown reader ID"))
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
                Assert.Fail($"the worker still serves reader {readerId} after it was suspended");

            Thread.Sleep(50);
        }
    }
}
