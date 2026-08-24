using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Tests;

[TestFixture]
public sealed class FFmpegErrorCodeExtractorTests
{
    // Telemetry signature from the v2.0.0-preview.6 crash reports: a truncated / headerless mp4
    // (moov atom not found) surfaces as AVERROR_INVALIDDATA.
    private const int InvalidDataCode = -1094995529;

    // FFmpegException(error) コンストラクタは av_strerror でメッセージを生成するためネイティブが
    // 必要。EncodingCancellationTests と同じく、ロードできない環境では自己スキップする。
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

        // ThrowIfError (MediaDemuxer.Open などの実経路) と同じく FFmpegException(error) を使う。
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
        // プレーン文字列コンストラクタはコードを持たない (ThrowIfError 経由ではない)。
        // ネイティブ非依存のためネイティブの有無に関わらず検証できる。
        Assert.That(
            FFmpegErrorCodeExtractor.TryGetFFmpegErrorCode(new FFmpegException("manual message")),
            Is.Null);
    }
}
