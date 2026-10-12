using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Tests;

[TestFixture]
public sealed class FFmpegErrorCodeExtractorTests
{
    // Signature: truncated MP4 without a moov atom.
    private const int InvalidDataCode = -1094995529;

    [Test]
    public void TryGetFFmpegErrorCode_ReturnsAVErrorCodeFromThrowIfErrorException()
    {
        FFmpegTestHelpers.RequireFFmpeg();

        // Match the constructor used by ThrowIfError.
        var ex = new FFmpegException(InvalidDataCode);

        Assert.That(FFmpegErrorCodeExtractor.TryGetFFmpegErrorCode(ex), Is.EqualTo(InvalidDataCode));
    }

    [Test]
    public void TryGetFFmpegErrorCode_WalksInnerExceptions()
    {
        FFmpegTestHelpers.RequireFFmpeg();

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
        // The string constructor has no error code.
        Assert.That(
            FFmpegErrorCodeExtractor.TryGetFFmpegErrorCode(new FFmpegException("manual message")),
            Is.Null);
    }
}
