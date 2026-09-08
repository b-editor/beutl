using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Tests;

[TestFixture]
public sealed class FFmpegErrorCodeExtractorTests
{
    // Signature: truncated MP4 without a moov atom.
    private const int InvalidDataCode = -1094995529;

    // Skip native-dependent checks when FFmpeg is unavailable.
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
            return false; // Assert.Ignore always throws.
        }
    }

    [Test]
    public void TryGetFFmpegErrorCode_ReturnsAVErrorCodeFromThrowIfErrorException()
    {
        if (!RequireFFmpeg())
            return;

        // Match the constructor used by ThrowIfError.
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
        // The string constructor has no error code.
        Assert.That(
            FFmpegErrorCodeExtractor.TryGetFFmpegErrorCode(new FFmpegException("manual message")),
            Is.Null);
    }
}
