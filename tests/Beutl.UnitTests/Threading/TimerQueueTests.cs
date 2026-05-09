using Beutl.Threading;
using Microsoft.Extensions.Time.Testing;

namespace Beutl.UnitTests.Threading;

public class TimerQueueTests
{
    [Test]
    public void Next_OnEmpty_ReturnsNull()
    {
        var time = new FakeTimeProvider();
        var queue = new TimerQueue(time);

        Assert.That(queue.Next, Is.Null);
    }

    [Test]
    public void Next_ReturnsSmallestTimestamp()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var queue = new TimerQueue(time);

        DateTimeOffset later = time.GetUtcNow().AddSeconds(60);
        DateTimeOffset earlier = time.GetUtcNow().AddSeconds(10);

        queue.Enqueue(later, DispatchPriority.Medium, () => { }, CancellationToken.None);
        queue.Enqueue(earlier, DispatchPriority.Medium, () => { }, CancellationToken.None);

        Assert.That(queue.Next, Is.EqualTo(earlier));
    }

    [Test]
    public void TryDequeue_DoesNothingWhenTimestampInFuture()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var queue = new TimerQueue(time);
        DateTimeOffset future = time.GetUtcNow().AddMinutes(5);

        queue.Enqueue(future, DispatchPriority.Medium, () => { }, CancellationToken.None);

        Assert.That(queue.TryDequeue(out var ops), Is.False);
        Assert.That(ops, Is.Null);
    }

    [Test]
    public void TryDequeue_ReturnsOperationsForReachedTimestamp()
    {
        var start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var time = new FakeTimeProvider(start);
        var queue = new TimerQueue(time);
        DateTimeOffset stamp = start.AddSeconds(1);

        queue.Enqueue(stamp, DispatchPriority.Low, () => { }, CancellationToken.None);
        queue.Enqueue(stamp, DispatchPriority.High, () => { }, CancellationToken.None);

        // Not yet reached.
        Assert.That(queue.TryDequeue(out _), Is.False);

        time.Advance(TimeSpan.FromSeconds(2));

        Assert.That(queue.TryDequeue(out var ops), Is.True);
        Assert.That(ops, Has.Count.EqualTo(2));
    }

    [Test]
    public void Enqueue_SameTimestampAccumulates()
    {
        var time = new FakeTimeProvider();
        var queue = new TimerQueue(time);
        DateTimeOffset stamp = time.GetUtcNow().AddSeconds(1);

        queue.Enqueue(stamp, DispatchPriority.Medium, () => { }, CancellationToken.None);
        queue.Enqueue(stamp, DispatchPriority.Medium, () => { }, CancellationToken.None);
        queue.Enqueue(stamp, DispatchPriority.Medium, () => { }, CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(2));

        Assert.That(queue.TryDequeue(out var ops), Is.True);
        Assert.That(ops, Has.Count.EqualTo(3));
    }
}
