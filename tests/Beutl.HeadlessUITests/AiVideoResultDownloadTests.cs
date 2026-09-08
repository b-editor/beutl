using Beutl.Services.AI;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AiVideoResultDownloadTests
{
    [Test]
    public async Task BoundedStream_RejectsOverflowAndLeavesTheStagingStreamOpen()
    {
        using var destination = new MemoryStream();
        using Stream bounded = AiVideoResultDownload.CreateBoundedStream(
            destination,
            maximumBytes: 4);

        await bounded.WriteAsync(new byte[] { 1, 2, 3, 4 });
        Assert.ThrowsAsync<InvalidDataException>(async () =>
            await bounded.WriteAsync(new byte[] { 5 }));
        bounded.Dispose();

        Assert.DoesNotThrow(() => destination.WriteByte(6));
        Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 6 }));
    }
}
