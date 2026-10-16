using System.Diagnostics;
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
        DecodingWorkerTestHelpers.RequireDecodingWorker();
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

    // After the shared worker exits, for example in a crash, the host starts a new one on the next open. A reader
    // opened on the old worker must move to the new one on its next read instead of failing until reopened.
    [Test]
    public void Reader_MovesToTheNewWorker_AfterTheWorkerExits()
    {
        using var reader = OpenFixture();
        byte[] before = ReadFramePixels(reader, 0);
        int exitedPid = FFmpegWorkerProcess.DecodingInstance.WorkerPid;
        KillWorker(exitedPid);

        byte[] after = ReadFramePixels(reader, 0);
        Assert.Multiple(() =>
        {
            Assert.That(after, Is.EqualTo(before), "the reopened reader must decode the same frame");
            Assert.That(FFmpegWorkerProcess.DecodingInstance.WorkerPid, Is.Not.EqualTo(exitedPid));
            Assert.That(reader.IsSuspended, Is.False);
        });
    }

    // A file that crashes the background worker must not reach the editor's readers.
    [Test]
    public void BackgroundWorkerExit_LeavesTheEditorReaderOnItsWorker()
    {
        using var editorReader = OpenFixture();
        using var backgroundReader = OpenFixture(background: true);
        byte[] before = ReadFramePixels(backgroundReader, 0);
        int editorPid = FFmpegWorkerProcess.DecodingInstance.WorkerPid;
        int editorReaderId = editorReader.ReaderId;
        int exitedPid = FFmpegWorkerProcess.BackgroundDecodingInstance.WorkerPid;
        Assert.That(exitedPid, Is.Not.EqualTo(editorPid));
        KillWorker(exitedPid);

        ReadFramePixels(editorReader, 0);
        byte[] after = ReadFramePixels(backgroundReader, 0);
        Assert.Multiple(() =>
        {
            Assert.That(editorReader.ReaderId, Is.EqualTo(editorReaderId), "the editor reader must not reopen");
            Assert.That(FFmpegWorkerProcess.DecodingInstance.WorkerPid, Is.EqualTo(editorPid));
            Assert.That(after, Is.EqualTo(before), "the background reader must reopen on a new background worker");
            Assert.That(FFmpegWorkerProcess.BackgroundDecodingInstance.WorkerPid, Is.Not.EqualTo(exitedPid).And.Not.EqualTo(editorPid));
        });
    }

    private static void KillWorker(int pid)
    {
        using var worker = Process.GetProcessById(pid);
        worker.Kill();
        Assert.That(worker.WaitForExit(10_000), Is.True);
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

    private static FFmpegReaderProxy OpenFixture(string? path = null, bool background = false)
    {
        var decoderInfo = new FFmpegDecoderInfo(new FFmpegDecodingSettings());
        MediaReader? reader = decoderInfo.Open(
            path ?? FixturePath, new MediaOptions(MediaMode.Video) { IsBackground = background });
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
