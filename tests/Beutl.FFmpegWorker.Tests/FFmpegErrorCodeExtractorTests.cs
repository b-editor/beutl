using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Tests;

[TestFixture]
public sealed class FFmpegErrorCodeExtractorTests
{
    // Telemetry signature from the v2.0.0-preview.6 crash reports: a truncated / headerless mp4
    // (moov atom not found) surfaces as AVERROR_INVALIDDATA.
    private const int InvalidDataCode = -1094995529;

    // The FFmpegException(error) constructor builds its message through av_strerror, which needs
    // the FFmpeg natives. Like EncodingCancellationTests, self-skip when they cannot be loaded.
    private static bool RequireFFmpeg()
    {
        try
        {
            FFmpegLoaderWorker.Initialize();
            return true;
        }
        catch (Exception)
        {
            Assert.Ignore("FFmpeg shared libraries unavailable; skipping the native-dependent assertion.");
            return false; // unreachable, Assert.Ignore throws
        }
    }

    [Test]
    public void TryGetFFmpegErrorCode_ReturnsAVErrorCodeFromThrowIfErrorException()
    {
        if (!RequireFFmpeg())
            return;

        // Use FFmpegException(error), exactly like ThrowIfError does on real paths such as
        // MediaDemuxer.Open.
        var ex = new FFmpegException(InvalidDataCode);

        Assert.That(FFmpegErrorCodeExtractor.TryGetFFmpegErrorCode(ex), Is.EqualTo(InvalidDataCode));
    }

    [Test]
    public void TryGetFFmpegErrorCode_WalksInnerExceptions()
    {
        if (!RequireFFmpeg())
            return;

        var inner = new FFmpegException(InvalidDataCode);
        var outer = new InvalidOperationException("wrapper", inner);

        Assert.That(FFmpegErrorCodeExtractor.TryGetFFmpegErrorCode(outer), Is.EqualTo(InvalidDataCode));
    }

    [Test]
    public void TryGetFFmpegErrorCode_NonFFmpegException_ReturnsNull()
    {
        Assert.That(
            FFmpegErrorCodeExtractor.TryGetFFmpegErrorCode(new IOException("disk full")),
            Is.Null);
    }

    [Test]
    public void TryGetFFmpegErrorCode_PlainStringFFmpegException_ReturnsNull()
    {
        // The plain-string constructor carries no code (it is not produced by ThrowIfError).
        // This is native-independent, so it can be verified regardless of native availability.
        Assert.That(
            FFmpegErrorCodeExtractor.TryGetFFmpegErrorCode(new FFmpegException("manual message")),
            Is.Null);
    }
}
