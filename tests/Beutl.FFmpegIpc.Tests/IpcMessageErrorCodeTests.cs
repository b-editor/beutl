using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Transport;

namespace Beutl.FFmpegIpc.Tests;

[TestFixture]
public sealed class IpcMessageErrorCodeTests
{
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
        Assert.That(received.Error, Is.EqualTo(msg.Error));
    }
}
