using Beutl.Extensions.FFmpeg;
using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.FFmpegIpc;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.Transport;
using Beutl.Media.Decoding;

namespace Beutl.FFmpegIpc.Tests;

/// <summary>Reproduces opening an MP4 without a moov atom.</summary>
[TestFixture, NonParallelizable]
public class FFmpegReaderProxyOpenContractTests
{
    private string _moovMissingPath = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _moovMissingPath = Path.Combine(
            Path.GetTempPath(), $"beutl-ffmpeg-moovmissing-{Guid.NewGuid():N}.mp4");
        // ftyp only; no moov atom.
        File.WriteAllBytes(_moovMissingPath,
        [
            0x00, 0x00, 0x00, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p',
            (byte)'i', (byte)'s', (byte)'o', (byte)'m',
            0x00, 0x00, 0x00, 0x01,
            (byte)'i', (byte)'s', (byte)'o', (byte)'m', (byte)'m', (byte)'p', (byte)'4', (byte)'1',
        ]);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        try
        {
            if (_moovMissingPath != null && File.Exists(_moovMissingPath))
                File.Delete(_moovMissingPath);
        }
        catch (Exception)
        {
            // Best-effort cleanup.
        }
    }

    [Test]
    public void Open_Mp4WithoutMoovAtom_ReturnsNullAndWorkerReportsInvalidData()
    {
        if (!WorkerProbe.WorkerBinaryPresent())
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

        var decoderInfo = new FFmpegDecoderInfo(new FFmpegDecodingSettings());

        MediaReader? reader = null;
        Assert.That(
            () => reader = decoderInfo.Open(_moovMissingPath, new MediaOptions(MediaMode.Video)),
            Throws.Nothing,
            "A corrupt input file must degrade to a null reader (log + skip), never a thrown crash.");
        reader?.Dispose();

        Assert.That(reader, Is.Null,
            "An mp4 without a moov atom cannot be opened; FFmpeg rejects it with AVERROR_INVALIDDATA.");
    }

    [Test]
    public void OpenFile_Mp4WithoutMoovAtom_WorkerReportsInvalidDataErrorCode()
    {
        if (!WorkerProbe.WorkerBinaryPresent())
        {
            Assert.Ignore("FFmpeg worker binary not present in the test output; skipping.");
        }

        IpcConnection connection = GetConnectionOrIgnore();

        var request = new OpenFileRequest
        {
            FilePath = _moovMissingPath,
            StreamsToLoad = (int)MediaMode.Video,
        };

        // Check the worker error code before FFmpegDecoderInfo swallows the exception.
        var thrown = Assert.Throws<FFmpegWorkerException>(() =>
            connection.RequestAsync<OpenFileRequest, OpenFileResponse>(
                MessageType.OpenFile, MessageType.OpenFileResult, request)
                .GetAwaiter().GetResult());

        Assert.That(thrown!.FFmpegErrorCode, Is.EqualTo(FFmpegErrorMessageMapper.InvalidDataCode),
            "The worker must surface AVERROR_INVALIDDATA as a structured error code, not just text.");
    }

    private static class WorkerProbe
    {
        public static bool WorkerBinaryPresent()
        {
            string baseDir = AppContext.BaseDirectory;
            return File.Exists(Path.Combine(baseDir, "Beutl.FFmpegWorker.dll"))
                || File.Exists(Path.Combine(baseDir, "FFmpegWorker", "Beutl.FFmpegWorker.dll"));
        }
    }

    private static IpcConnection GetConnectionOrIgnore()
    {
        try
        {
            return FFmpegWorkerProcess.DecodingInstance.EnsureStarted();
        }
        catch (FFmpegLibrariesNotFoundException ex)
        {
            Assert.Ignore($"FFmpeg natives unavailable ({ex.Message}); skipping.");
            throw; // Assert.Ignore always throws.
        }
    }
}
