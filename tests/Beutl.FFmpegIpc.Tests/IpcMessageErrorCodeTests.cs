using System.Text.Json;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Transport;

namespace Beutl.FFmpegIpc.Tests;

[TestFixture]
public sealed class IpcMessageErrorCodeTests
{
    [Test]
    public void CreateError_WithErrorCode_RoundTripsErrorCode()
    {
        var msg = IpcMessage.CreateError(7, "FFmpeg error [-1094995529] Invalid data found when processing input",
            "stack", errorCode: -1094995529);

        string json = JsonSerializer.Serialize(msg);
        var deserialized = JsonSerializer.Deserialize<IpcMessage>(json);

        Assert.That(deserialized, Is.Not.Null);
        Assert.That(deserialized!.ErrorCode, Is.EqualTo(-1094995529));
        Assert.That(deserialized.Error, Is.EqualTo(msg.Error));
    }

    [Test]
    public async Task CreateError_WithErrorCode_RoundTripsThroughSourceGeneratedSerializer()
    {
        // Use the production wire serializer.
        var msg = IpcMessage.CreateError(9, "boom", "stack", errorCode: -1094995529);

        var stream = new MemoryStream();
        await MessageSerializer.WriteMessageAsync(stream, msg);
        stream.Position = 0;
        IpcMessage? received = await MessageSerializer.ReadMessageAsync(stream);

        Assert.That(received, Is.Not.Null);
        Assert.That(received!.ErrorCode, Is.EqualTo(-1094995529));
    }

    [Test]
    public void CreateError_WithoutErrorCode_KeepsErrorCodeNull()
    {
        // Existing calls leave ErrorCode null.
        var msg = IpcMessage.CreateError(1, "plain error");

        Assert.That(msg.ErrorCode, Is.Null);
    }
}
