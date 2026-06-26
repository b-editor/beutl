using Beutl.FFmpegIpc.Providers;

namespace Beutl.FFmpegIpc.Tests;

/// <summary>
/// Pins the seek/prefetch slot mechanics both IPC providers now share: a matching key consumes the armed
/// prefetch, any other key drains it as stale, and detaching always clears the slot before the caller awaits.
/// </summary>
[TestFixture]
public class PrefetchSlotTests
{
    [Test]
    public void NewSlot_IsEmpty()
    {
        var slot = new PrefetchSlot<long, int>();

        Assert.Multiple(() =>
        {
            Assert.That(slot.HasPrefetch, Is.False);
            Assert.That(slot.IsFaulted, Is.False);
            Assert.That(slot.Detach(), Is.Null);
            Assert.That(slot.TryConsumeMatching(0, out _), Is.Null);
            Assert.That(slot.TryDetachStale(0), Is.Null);
        });
    }

    [Test]
    public void Arm_MarksSlotOccupied()
    {
        var slot = new PrefetchSlot<long, int>();
        slot.Arm(7, bufferIndex: 1, Task.FromResult(42));

        Assert.That(slot.HasPrefetch, Is.True);
    }

    [Test]
    public void Arm_WhenAlreadyArmed_Throws()
    {
        // Overwriting an armed prefetch would strand its undrained IPC response, so a double-arm must fail
        // loudly in every build config (a runtime throw, not a Debug.Assert compiled out in Release).
        var slot = new PrefetchSlot<long, int>();
        slot.Arm(7, bufferIndex: 1, Task.FromResult(42));

        Assert.Multiple(() =>
        {
            Assert.That(() => slot.Arm(8, bufferIndex: 0, Task.FromResult(99)),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(slot.HasPrefetch, Is.True, "the rejected re-arm must leave the original prefetch intact");
        });
    }

    [Test]
    public async Task TryConsumeMatching_OnMatchingKey_DetachesTaskAndBufferIndexAndClears()
    {
        var slot = new PrefetchSlot<long, int>();
        var task = Task.FromResult(42);
        slot.Arm(7, bufferIndex: 1, task);

        Task<int>? consumed = slot.TryConsumeMatching(7, out int bufferIndex);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.SameAs(task));
            Assert.That(bufferIndex, Is.EqualTo(1));
            Assert.That(slot.HasPrefetch, Is.False, "a consumed slot is cleared");
        });
        Assert.That(await consumed!, Is.EqualTo(42));
    }

    [Test]
    public void TryConsumeMatching_OnDifferentKey_ReturnsNullAndLeavesSlotArmed()
    {
        var slot = new PrefetchSlot<long, int>();
        slot.Arm(7, bufferIndex: 1, Task.FromResult(42));

        Task<int>? consumed = slot.TryConsumeMatching(8, out _);

        Assert.Multiple(() =>
        {
            Assert.That(consumed, Is.Null);
            Assert.That(slot.HasPrefetch, Is.True, "a non-matching consume must not drop the armed prefetch");
        });
    }

    [Test]
    public void TryDetachStale_OnDifferentKey_DetachesTaskAndClears()
    {
        var slot = new PrefetchSlot<long, int>();
        var task = Task.FromResult(42);
        slot.Arm(7, bufferIndex: 1, task);

        Task<int>? stale = slot.TryDetachStale(8);

        Assert.Multiple(() =>
        {
            Assert.That(stale, Is.SameAs(task));
            Assert.That(slot.HasPrefetch, Is.False, "a drained stale slot is cleared");
        });
    }

    [Test]
    public void TryDetachStale_OnMatchingKey_ReturnsNullAndLeavesSlotArmed()
    {
        var slot = new PrefetchSlot<long, int>();
        slot.Arm(7, bufferIndex: 1, Task.FromResult(42));

        Task<int>? stale = slot.TryDetachStale(7);

        Assert.Multiple(() =>
        {
            Assert.That(stale, Is.Null, "a matching key is a hit, not stale");
            Assert.That(slot.HasPrefetch, Is.True);
        });
    }

    [Test]
    public void Detach_ReturnsArmedTaskAndClears()
    {
        var slot = new PrefetchSlot<long, int>();
        var task = Task.FromResult(42);
        slot.Arm(7, bufferIndex: 1, task);

        Task<int>? detached = slot.Detach();

        Assert.Multiple(() =>
        {
            Assert.That(detached, Is.SameAs(task));
            Assert.That(slot.HasPrefetch, Is.False);
            Assert.That(slot.Detach(), Is.Null, "a second detach is a no-op");
        });
    }

    [Test]
    public async Task IsFaulted_ReflectsArmedTaskFaultState()
    {
        var slot = new PrefetchSlot<long, int>();
        var tcs = new TaskCompletionSource<int>();
        slot.Arm(7, bufferIndex: 0, tcs.Task);

        Assert.That(slot.IsFaulted, Is.False, "a pending prefetch is not faulted");

        tcs.SetException(new InvalidOperationException("boom"));
        // Observe the fault so it does not surface as an UnobservedTaskException at GC.
        try { await tcs.Task; } catch (InvalidOperationException) { }

        Assert.That(slot.IsFaulted, Is.True, "a faulted prefetch reports IsFaulted");
    }
}
