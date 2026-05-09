using Beutl.Threading;

namespace Beutl.UnitTests.Threading;

public class OperationQueueTests
{
    [Test]
    public void Enqueue_TryDequeue_ReturnsItemsByPriority()
    {
        var queue = new OperationQueue();

        var low = NewOperation("low", DispatchPriority.Low);
        var medium = NewOperation("medium", DispatchPriority.Medium);
        var high = NewOperation("high", DispatchPriority.High);

        queue.Enqueue(low);
        queue.Enqueue(medium);
        queue.Enqueue(high);

        Assert.That(queue.TryDequeue(out var first), Is.True);
        Assert.That(first, Is.SameAs(high));

        Assert.That(queue.TryDequeue(out var second), Is.True);
        Assert.That(second, Is.SameAs(medium));

        Assert.That(queue.TryDequeue(out var third), Is.True);
        Assert.That(third, Is.SameAs(low));
    }

    [Test]
    public void TryDequeue_OnEmpty_ReturnsFalse()
    {
        var queue = new OperationQueue();

        Assert.That(queue.TryDequeue(out var op), Is.False);
        Assert.That(op, Is.Null);
    }

    [Test]
    public void Count_OnlyCountsPriorityAtOrAboveMinimum()
    {
        var queue = new OperationQueue();

        queue.Enqueue(NewOperation("a", DispatchPriority.Low));
        queue.Enqueue(NewOperation("b", DispatchPriority.Medium));
        queue.Enqueue(NewOperation("c", DispatchPriority.Medium));
        queue.Enqueue(NewOperation("d", DispatchPriority.High));

        Assert.That(queue.Count(DispatchPriority.Low), Is.EqualTo(4));
        Assert.That(queue.Count(DispatchPriority.Medium), Is.EqualTo(3));
        Assert.That(queue.Count(DispatchPriority.High), Is.EqualTo(1));
    }

    [Test]
    public void Any_ReflectsPresenceAtOrAboveMinimum()
    {
        var queue = new OperationQueue();

        Assert.That(queue.Any(DispatchPriority.Low), Is.False);

        queue.Enqueue(NewOperation("a", DispatchPriority.Low));

        Assert.That(queue.Any(DispatchPriority.Low), Is.True);
        Assert.That(queue.Any(DispatchPriority.Medium), Is.False);
    }

    [Test]
    public void Enqueue_PreservesFifoWithinSamePriority()
    {
        var queue = new OperationQueue();
        var first = NewOperation("first", DispatchPriority.Medium);
        var second = NewOperation("second", DispatchPriority.Medium);

        queue.Enqueue(first);
        queue.Enqueue(second);

        Assert.That(queue.TryDequeue(out var dequeued1), Is.True);
        Assert.That(dequeued1, Is.SameAs(first));
        Assert.That(queue.TryDequeue(out var dequeued2), Is.True);
        Assert.That(dequeued2, Is.SameAs(second));
    }

    private static DispatcherOperation NewOperation(string label, DispatchPriority priority)
    {
        return new DispatcherOperation(() => { _ = label; }, priority, CancellationToken.None);
    }
}
