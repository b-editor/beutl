namespace Beutl.FFmpegIpc.Tests;

[TestFixture]
public sealed class FFmpegErrorMessageMapperTests
{
    [Test]
    public void TryClassify_KnownCodes_MapToStableKinds()
    {
        Assert.That(
            FFmpegErrorMessageMapper.TryClassify(FFmpegErrorMessageMapper.InvalidDataCode),
            Is.EqualTo(FFmpegErrorKind.InvalidData));
        Assert.That(
            FFmpegErrorMessageMapper.TryClassify(FFmpegErrorMessageMapper.DecoderNotFoundCode),
            Is.EqualTo(FFmpegErrorKind.DecoderNotFound));
        Assert.That(
            FFmpegErrorMessageMapper.TryClassify(FFmpegErrorMessageMapper.DemuxerNotFoundCode),
            Is.EqualTo(FFmpegErrorKind.DemuxerNotFound));
        Assert.That(
            FFmpegErrorMessageMapper.TryClassify(FFmpegErrorMessageMapper.ProtocolNotFoundCode),
            Is.EqualTo(FFmpegErrorKind.ProtocolNotFound));
        Assert.That(
            FFmpegErrorMessageMapper.TryClassify(FFmpegErrorMessageMapper.StreamNotFoundCode),
            Is.EqualTo(FFmpegErrorKind.StreamNotFound));
    }

    [Test]
    public void TryClassify_WithoutAKnownCode_ReturnsNull()
    {
        Assert.That(FFmpegErrorMessageMapper.TryClassify(null), Is.Null);
        Assert.That(FFmpegErrorMessageMapper.TryClassify(-1), Is.Null);
    }
}
