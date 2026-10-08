using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics;

[TestFixture]
public class ThumbnailStripGateTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task EntrantBeyondCapacity_WaitsUntilASlotIsReleased()
    {
        var gate = new ThumbnailStripGate(2);
        Assert.That(await gate.TryEnterAsync(CancellationToken.None), Is.True);
        Assert.That(await gate.TryEnterAsync(CancellationToken.None), Is.True);

        Task<bool> third = gate.TryEnterAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.That(third.IsCompleted, Is.False);

        gate.Release();

        Assert.That(await third.WaitAsync(s_timeout), Is.True);
        Assert.That(gate.AvailableSlots, Is.Zero);
    }

    [Test]
    public async Task CancelledWaiter_ReturnsFalse_AndTakesNoSlot()
    {
        var gate = new ThumbnailStripGate(1);
        Assert.That(await gate.TryEnterAsync(CancellationToken.None), Is.True);

        using var cts = new CancellationTokenSource();
        Task<bool> waiter = gate.TryEnterAsync(cts.Token);
        cts.Cancel();

        Assert.That(await waiter.WaitAsync(s_timeout), Is.False);

        gate.Release();
        Assert.That(gate.AvailableSlots, Is.EqualTo(1));
    }
}

// Uses the process-wide gate of SourceVideo, so it must not overlap other strip enumerations.
[TestFixture]
[NonParallelizable]
public class SourceVideoThumbnailStripGateTests
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task StripsThatEndWithoutYielding_ReleaseTheirSlot()
    {
        int capacity = SourceVideo.StripGate.AvailableSlots;

        // More strips than slots: a leaked slot would block the last ones until the timeout.
        for (int i = 0; i < capacity + 3; i++)
        {
            Assert.That(await ConsumeStripAsync(CreateDrawable(), CancellationToken.None).WaitAsync(s_timeout), Is.Zero);
        }

        Assert.That(SourceVideo.StripGate.AvailableSlots, Is.EqualTo(capacity));
    }

    [Test]
    public async Task StripCancelledWhileWaitingForASlot_EndsWithoutTakingOne()
    {
        int capacity = SourceVideo.StripGate.AvailableSlots;
        for (int i = 0; i < capacity; i++)
            Assert.That(await SourceVideo.StripGate.TryEnterAsync(CancellationToken.None), Is.True);

        try
        {
            using var cts = new CancellationTokenSource();
            Task<int> strip = ConsumeStripAsync(CreateDrawable(), cts.Token);
            await Task.Delay(50);
            Assert.That(strip.IsCompleted, Is.False, "the strip must wait while every slot is held");

            cts.Cancel();

            Assert.That(await strip.WaitAsync(s_timeout), Is.Zero);
            Assert.That(SourceVideo.StripGate.AvailableSlots, Is.Zero);
        }
        finally
        {
            for (int i = 0; i < capacity; i++)
                SourceVideo.StripGate.Release();
        }

        Assert.That(SourceVideo.StripGate.AvailableSlots, Is.EqualTo(capacity));
    }

    // A strip without a source ends right after taking its slot, exercising the same release path as a
    // consumer that stops early (both leave through the iterator's finally block).
    private static SourceVideo CreateDrawable()
        => new() { TimeRange = new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)) };

    private static async Task<int> ConsumeStripAsync(SourceVideo drawable, CancellationToken cancellationToken)
    {
        int count = 0;
        await foreach (var (_, _, thumbnail) in drawable.GetThumbnailStripAsync(
                           maxWidth: 100, maxHeight: 25, cancellationToken: cancellationToken))
        {
            thumbnail.Dispose();
            count++;
        }

        return count;
    }
}
