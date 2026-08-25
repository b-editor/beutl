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
        // MessageSerializer (production wire path) serializes via IpcJsonContext, whose source
        // generator must emit the nullable ErrorCode scalar; a reflection fallback would fail
        // under trimming/AOT. Verify the actual wire round-trip, not just the default serializer.
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
        // Backward compatibility: existing two/three-argument calls keep ErrorCode == null.
        var msg = IpcMessage.CreateError(1, "plain error");

        Assert.That(msg.ErrorCode, Is.Null);
    }
}
