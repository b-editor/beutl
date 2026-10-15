using Beutl.Threading;

namespace Beutl.UnitTests.Threading;

public class OperationQueueTests
{
    [Test]
    public void TryDequeue_OnEmpty_ReturnsFalse()
    {
        var queue = new OperationQueue();

        Assert.That(queue.TryDequeue(out var op), Is.False);
        Assert.That(op, Is.Null);
    }

    [Test]
    public void Any_ReflectsPresenceAtOrAboveMinimum()
    {
        var queue = new OperationQueue();

        Assert.That(queue.Any(DispatchPriority.Low), Is.False);

        queue.Enqueue(NewOperation(DispatchPriority.Low));

        Assert.That(queue.Any(DispatchPriority.Low), Is.True);
        Assert.That(queue.Any(DispatchPriority.Medium), Is.False);
    }

    [Test]
    public void TryDequeue_OrdersByPriorityThenFifoWhenInterleaved()
    {
        var queue = new OperationQueue();

        var medium1 = NewOperation(DispatchPriority.Medium);
        var high1 = NewOperation(DispatchPriority.High);
        var low1 = NewOperation(DispatchPriority.Low);
        var medium2 = NewOperation(DispatchPriority.Medium);
        var high2 = NewOperation(DispatchPriority.High);

        queue.Enqueue(medium1);
        queue.Enqueue(high1);
        queue.Enqueue(low1);
        queue.Enqueue(medium2);
        queue.Enqueue(high2);

        Assert.That(Drain(queue), Is.EqualTo(new[] { high1, high2, medium1, medium2, low1 }));
    }

    private static DispatcherOperation[] Drain(OperationQueue queue)
    {
        var result = new List<DispatcherOperation>();
        while (queue.TryDequeue(out var operation))
        {
            result.Add(operation);
        }

        return [.. result];
    }

    private static DispatcherOperation NewOperation(DispatchPriority priority)
    {
        return new DispatcherOperation(() => { }, priority, CancellationToken.None);
    }
}
