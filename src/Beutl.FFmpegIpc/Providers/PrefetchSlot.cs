namespace Beutl.FFmpegIpc.Providers;

/// <summary>
/// Single-slot double-buffer prefetch state shared by the IPC client providers
/// (<see cref="IpcFrameProvider"/>, <see cref="IpcSampleProvider"/>). Holds at most one in-flight
/// prefetch keyed by <typeparamref name="TKey"/> (a frame index or chunk offset) together with the
/// shared-memory buffer slot it targets, and encodes the seek/prefetch decision both providers used to
/// hand-duplicate: a request whose key matches the armed prefetch consumes it; any other key drains the
/// stale prefetch (await-and-discard) before a fresh request is issued. On a non-multiplexed connection
/// responses are read in send order, so draining the stale prefetch is what stops a fresh request from
/// reading the old response and tripping an id mismatch.
/// </summary>
/// <remarks>
/// The slot owns only the <see cref="Task{TResult}"/> handle and the (key, bufferIndex) metadata; awaiting
/// the detached task and observing/disposing its result is the caller's responsibility (a frame yields a
/// managed message with nothing to release, a sample yields a native buffer the caller must dispose when it
/// is discarded). Detaching clears the slot before the caller awaits, so a faulted prefetch cannot pin the
/// provider to a perpetually re-throwing task. Not thread-safe: callers drive it from a single logical
/// request sequence.
/// </remarks>
internal sealed class PrefetchSlot<TKey, TValue>
    where TKey : IEquatable<TKey>
{
    private Task<TValue>? _task;
    private TKey _key = default!;
    private int _bufferIndex;

    public bool HasPrefetch => _task is not null;

    // Test/diagnostic probe: true only while an armed prefetch has already faulted.
    public bool IsFaulted => _task?.IsFaulted == true;

    /// <summary>Arms the slot with an in-flight prefetch. The caller guarantees the slot is empty.</summary>
    public void Arm(TKey key, int bufferIndex, Task<TValue> task)
    {
        _key = key;
        _bufferIndex = bufferIndex;
        _task = task;
    }

    /// <summary>
    /// If a prefetch is armed for <paramref name="key"/>, detaches and returns it (clearing the slot) and
    /// reports the buffer slot it targeted; otherwise returns <c>null</c> and <paramref name="bufferIndex"/>
    /// is unspecified.
    /// </summary>
    public Task<TValue>? TryConsumeMatching(TKey key, out int bufferIndex)
    {
        if (_task is not null && _key.Equals(key))
        {
            bufferIndex = _bufferIndex;
            return Detach();
        }

        bufferIndex = default;
        return null;
    }

    /// <summary>
    /// If a prefetch is armed but for a different key than <paramref name="key"/>, detaches and returns it
    /// (clearing the slot) so the caller can drain it before issuing a fresh request; returns <c>null</c>
    /// when the slot is empty or already matches <paramref name="key"/>.
    /// </summary>
    public Task<TValue>? TryDetachStale(TKey key)
    {
        if (_task is not null && !_key.Equals(key))
            return Detach();

        return null;
    }

    /// <summary>Detaches whatever prefetch is armed (clearing the slot), or returns <c>null</c> if empty.</summary>
    public Task<TValue>? Detach()
    {
        Task<TValue>? task = _task;
        _task = null;
        return task;
    }
}
