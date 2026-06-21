using Beutl.Models;

namespace Beutl.UnitTests.Models;

[TestFixture]
public class BufferedPlayerWaitGateTests
{
    [Test]
    public void Publish_WhenStopRequestedAfterPublish_ClearsTokenAndSkipsWait()
    {
        using var cts = new CancellationTokenSource();
        var gate = new BufferedPlayerWaitGate(() => true);

        bool shouldWait = gate.Publish(cts);
        gate.Cancel();

        Assert.That(shouldWait, Is.False);
        Assert.That(cts.IsCancellationRequested, Is.False);
    }

    [Test]
    public void Clear_RemovesPublishedToken()
    {
        using var cts = new CancellationTokenSource();
        var gate = new BufferedPlayerWaitGate(() => false);

        bool shouldWait = gate.Publish(cts);
        gate.Clear(cts);
        gate.Cancel();

        Assert.That(shouldWait, Is.True);
        Assert.That(cts.IsCancellationRequested, Is.False);
    }

    [Test]
    public void Clear_DoesNotRemoveNewerPublishedToken()
    {
        using var oldCts = new CancellationTokenSource();
        using var newCts = new CancellationTokenSource();
        var gate = new BufferedPlayerWaitGate(() => false);

        gate.Publish(oldCts);
        gate.Publish(newCts);
        gate.Clear(oldCts);
        gate.Cancel();

        Assert.That(oldCts.IsCancellationRequested, Is.False);
        Assert.That(newCts.IsCancellationRequested, Is.True);
    }
}
