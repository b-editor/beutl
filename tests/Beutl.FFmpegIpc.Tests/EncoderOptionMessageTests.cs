using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;

namespace Beutl.FFmpegIpc.Tests;

[TestFixture]
public sealed class EncoderOptionMessageTests
{
    [Test]
    public void RequestPreservesDefaultCodecAndPixelFormat()
    {
        var message = IpcMessage.Create(7, MessageType.QueryEncoderOptions,
            new QueryEncoderOptionsRequest { OutputFile = "out.mp4", PixelFormat = 62 });
        var payload = message.GetPayload<QueryEncoderOptionsRequest>()!;
        Assert.That(payload.CodecName, Is.Null);
        Assert.That(payload.OutputFile, Is.EqualTo("out.mp4"));
        Assert.That(payload.PixelFormat, Is.EqualTo(62));
    }

    [Test]
    public void ResponsePreservesNativeChoicesRangesAndFallbackStatus()
    {
        var message = IpcMessage.Create(7, MessageType.QueryEncoderOptionsResult,
            new QueryEncoderOptionsResponse
            {
                Degraded = true,
                Options = [new EncoderOptionInfo
                {
                    Name = "profile", Kind = EncoderOptionKind.Choice, DefaultValue = "-1", Minimum = -1, Maximum = 100,
                    AllowsNumericValues = true, RequiresInteger = true,
                    Choices = [new EncoderOptionChoiceInfo { Value = "main", NumericValue = 1, Description = "Main profile" }],
                }],
            });
        var payload = message.GetPayload<QueryEncoderOptionsResponse>()!;
        Assert.That(payload.Degraded, Is.True);
        Assert.That(payload.Options[0].Kind, Is.EqualTo(EncoderOptionKind.Choice));
        Assert.That(payload.Options[0].Minimum, Is.EqualTo(-1));
        Assert.That(payload.Options[0].Maximum, Is.EqualTo(100));
        Assert.That(payload.Options[0].AllowsNumericValues, Is.True);
        Assert.That(payload.Options[0].RequiresInteger, Is.True);
        Assert.That(payload.Options[0].Choices[0].NumericValue, Is.EqualTo(1));
        Assert.That(payload.Options[0].Choices[0].Description, Is.EqualTo("Main profile"));
    }
}
