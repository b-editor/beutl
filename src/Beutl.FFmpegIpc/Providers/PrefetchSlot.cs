namespace Beutl.FFmpegIpc.Providers;

/// <summary>
/// Single-slot prefetch state of <see cref="IpcFrameProvider"/> and <see cref="IpcSampleProvider"/>: at most one
/// in-flight prefetch keyed by a frame index or chunk offset, with the buffer slot it targets. A request for the
/// armed key consumes it; any other request drains it first, since a non-multiplexed connection reads responses in
/// send order. Detaching clears the slot before the caller awaits and disposes the result. Not thread-safe.
/// </summary>
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
        // Overwriting an armed prefetch would strand its undrained IPC response, which a later request
        // would then read in place of its own and reject as an id mismatch.
        if (_task is not null)
            throw new InvalidOperationException("PrefetchSlot armed while a prefetch was already in flight.");

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
        if (_task is not null && EqualityComparer<TKey>.Default.Equals(_key, key))
        {
            bufferIndex = _bufferIndex;
            return Detach();
        }

        bufferIndex = default;
        return null;
    }

    /// <summary>Detaches whatever prefetch is armed (clearing the slot), or returns <c>null</c> if empty.</summary>
    public Task<TValue>? Detach()
    {
        Task<TValue>? task = _task;
        _task = null;
        _key = default!;
        _bufferIndex = 0;
        return task;
    }
}
