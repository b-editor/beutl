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

    // MediaReader's contract: a disposed reader produces no media (false), without reopening or racing
    // the asynchronous worker-side close.
    [TestCase(true)]
    [TestCase(false)]
    public void ReadAfterDispose_ReturnsFalseWithoutReopening(bool suspendFirst)
    {
        var reader = OpenFixture();
        if (suspendFirst)
        {
            var suspendable = (IIdleSuspendableReader)reader;
            Assert.That(suspendable.TrySuspend(suspendable.LastAccessTicks), Is.True);
        }

        int readerId = reader.ReaderId;
        reader.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(reader.ReadVideo(0, out var image), Is.False);
            Assert.That(image, Is.Null);
            Assert.That(reader.ReaderId, Is.EqualTo(readerId));
        });
    }

    [Test]
    public void FileReplacedWhileSuspended_IsNotAdopted()
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-ffmpeg-suspend-{Guid.NewGuid():N}.flv");
        File.Copy(FixturePath, path);
        try
        {
            using var reader = OpenFixture(path);
            int suspendedReaderId = reader.ReaderId;
            var suspendable = (IIdleSuspendableReader)reader;
            Assert.That(suspendable.TrySuspend(suspendable.LastAccessTicks), Is.True);
            AssertWorkerReaderClosed(suspendedReaderId);

            // Suspension released the file, so it can now be replaced with media of other streams.
            WriteSilentPcmWav(path);

            Assert.Multiple(() =>
            {
                Assert.That(reader.ReadVideo(0, out var image), Is.False);
                Assert.That(image, Is.Null);
                Assert.That(reader.ReadVideo(0, out _), Is.False, "a replaced file stays rejected");
                Assert.That(reader.ReaderId, Is.EqualTo(suspendedReaderId), "the reopened reader must not be adopted");
            });
        }
        finally
        {
            DeleteWhenReleased(path);
        }
    }

    [Test]
    public void ReopenFailure_ReturnsFalse_AndRetriesOnTheNextRead()
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-ffmpeg-suspend-{Guid.NewGuid():N}.flv");
        string movedPath = path + ".moved";
        File.Copy(FixturePath, path);
        try
        {
            using var reader = OpenFixture(path);
            byte[] before = ReadFramePixels(reader, 0);
            var suspendable = (IIdleSuspendableReader)reader;
            int suspendedReaderId = reader.ReaderId;
            Assert.That(suspendable.TrySuspend(suspendable.LastAccessTicks), Is.True);
            AssertWorkerReaderClosed(suspendedReaderId);

            MoveWhenReleased(path, movedPath);
            Assert.Multiple(() =>
            {
                Assert.That(() => reader.ReadVideo(0, out _), Throws.Nothing);
                Assert.That(reader.ReadVideo(0, out var image), Is.False);
                Assert.That(image, Is.Null);
                Assert.That(reader.IsSuspended, Is.True, "a failed reopen must leave the reader suspended");
            });

            File.Move(movedPath, path);
            Assert.That(ReadFramePixels(reader, 0), Is.EqualTo(before), "the reader must recover once the file is back");
        }
        finally
        {
            DeleteWhenReleased(path);
            DeleteWhenReleased(movedPath);
        }
    }

    // The worker forgets the reader ID before it finishes disposing the reader, and FFmpeg opens files
    // without delete sharing, so a move or delete right after the close can still hit the open handle.
    private static void MoveWhenReleased(string source, string destination)
        => RetryWhileLocked(() => File.Move(source, destination));

    private static void DeleteWhenReleased(string path)
    {
        try
        {
            RetryWhileLocked(() => File.Delete(path));
        }
        catch (IOException)
        {
            // Best-effort cleanup of a temp file; it must not hide the test's own outcome.
        }
    }

    private static void RetryWhileLocked(Action action)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (true)
        {
            try
            {
                action();
                return;
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(50);
            }
        }
    }

    private static FFmpegReaderProxy OpenFixture(string? path = null)
    {
        var decoderInfo = new FFmpegDecoderInfo(new FFmpegDecodingSettings());
        MediaReader? reader = decoderInfo.Open(path ?? FixturePath, new MediaOptions(MediaMode.Video));
        Assert.That(reader, Is.TypeOf<FFmpegReaderProxy>());
        return (FFmpegReaderProxy)reader!;
    }

    // 0.1 s of 8 kHz mono 16-bit silence: decodable by FFmpeg, but without a video stream.
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
