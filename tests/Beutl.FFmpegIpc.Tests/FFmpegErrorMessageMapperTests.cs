namespace Beutl.FFmpegIpc.Tests;

[TestFixture]
public sealed class FFmpegErrorMessageMapperTests
{
    // Signature: truncated MP4 without a moov atom.
    private const string InvalidDataMessage =
        "FFmpeg error [-1094995529] Invalid data found when processing input";

    [Test]
    public void TryClassify_KnownCodes_MapToStableKinds()
    {
        Assert.That(
            FFmpegErrorMessageMapper.TryClassify(FFmpegErrorMessageMapper.InvalidDataCode, null),
            Is.EqualTo(FFmpegErrorKind.InvalidData));
        Assert.That(
            FFmpegErrorMessageMapper.TryClassify(FFmpegErrorMessageMapper.DecoderNotFoundCode, null),
            Is.EqualTo(FFmpegErrorKind.DecoderNotFound));
        Assert.That(
            FFmpegErrorMessageMapper.TryClassify(FFmpegErrorMessageMapper.DemuxerNotFoundCode, null),
            Is.EqualTo(FFmpegErrorKind.DemuxerNotFound));
        Assert.That(
            FFmpegErrorMessageMapper.TryClassify(FFmpegErrorMessageMapper.ProtocolNotFoundCode, null),
            Is.EqualTo(FFmpegErrorKind.ProtocolNotFound));
        Assert.That(
            FFmpegErrorMessageMapper.TryClassify(FFmpegErrorMessageMapper.StreamNotFoundCode, null),
            Is.EqualTo(FFmpegErrorKind.StreamNotFound));
    }

    [Test]
    public void TryClassify_LegacyInvalidDataTextWithoutCode_ClassifiesAsInvalidData()
    {
        Assert.That(
            FFmpegErrorMessageMapper.TryClassify(null, InvalidDataMessage),
            Is.EqualTo(FFmpegErrorKind.InvalidData));
        Assert.That(FFmpegErrorMessageMapper.TryClassify(null, "some other error"), Is.Null);
    }
}
