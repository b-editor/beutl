using Beutl.FFmpegIpc;

namespace Beutl.UnitTests.FFmpegIpc;

[TestFixture]
public sealed class FFmpegErrorMessageMapperTests
{
    // Telemetry signature from the v2.0.0-preview.6 crash reports: a truncated / headerless mp4
    // (moov atom not found) surfaces as AVERROR_INVALIDDATA with this exact message.
    private const string InvalidDataMessage =
        "FFmpeg error [-1094995529] Invalid data found when processing input";

    [Test]
    public void Translate_InvalidDataCode_TranslatesToUserFacingText()
    {
        string? translated = FFmpegErrorMessageMapper.Translate(
            FFmpegErrorMessageMapper.InvalidDataCode, InvalidDataMessage);

        Assert.That(translated, Is.Not.Null);
        Assert.That(translated, Does.Contain("corrupt").Or.Contains("incomplete"));
        // The raw numeric code must not leak into the user-facing message.
        Assert.That(translated, Does.Not.Contain("-1094995529"));
    }

    [Test]
    public void Translate_InvalidDataTextWithoutCode_TranslatesViaMessageMatch()
    {
        // EncodeComplete など FFmpegErrorCode を載せない経路でも翻訳できること。

        string? translated = FFmpegErrorMessageMapper.Translate(null, InvalidDataMessage);

        Assert.That(translated, Is.Not.Null);
        Assert.That(translated, Does.Not.Contain("-1094995529"));
    }

    [Test]
    public void Translate_UnknownCodeOrMessage_ReturnsNull()
    {
        Assert.That(FFmpegErrorMessageMapper.Translate(null, "some other error"), Is.Null);
        Assert.That(FFmpegErrorMessageMapper.Translate(-1, null), Is.Null);
        Assert.That(
            FFmpegErrorMessageMapper.Translate(FFmpegErrorMessageMapper.ProtocolNotFoundCode, null),
            Is.Not.Null); // known code alone translates
    }

    [Test]
    public void Translate_WithFormat_InjectsDescriptionIntoFormat()
    {
        string? translated = FFmpegErrorMessageMapper.Translate(
            FFmpegErrorMessageMapper.InvalidDataCode, InvalidDataMessage, "Prefix: {0}");

        Assert.That(translated, Does.StartWith("Prefix: "));
    }
}
