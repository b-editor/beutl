namespace Beutl.Editor.Services.Captions;

/// <summary>
/// Publishes the immutable states of one caption registry, keeps each replaced state until its
/// leases drain, and retires every state on disposal. Readers lock <see cref="StateGate"/> while
/// they use <see cref="Current"/>, so a state cannot retire between a lookup and its lease.
/// </summary>
internal sealed class CaptionRegistryStates<TState>
    where TState : CaptionRegistryLeaseState
{
    private readonly object _mutationGate = new();
    private readonly HashSet<TState> _retiredStates = [];
    private readonly object _owner;
    private readonly Func<TState> _createEmpty;
    private readonly Action<TState> _publish;
    private TState _current;
    private Task? _disposeTask;
    private bool _disposed;

    public CaptionRegistryStates(
        object owner,
        TState initial,
        Func<TState> createEmpty,
        Action<TState> publish)
    {
        _owner = owner;
        _current = initial;
        _createEmpty = createEmpty;
        _publish = publish;
    }

    public object StateGate { get; } = new();

    public TState Current => _current;

    public CaptionRegistryDrain Replace(Func<TState> createNext)
    {
        lock (_mutationGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, _owner);
            return Swap(createNext());
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_mutationGate)
            return new ValueTask(_disposeTask ??= DisposeCoreAsync());
    }

    private Task DisposeCoreAsync()
    {
        if (_disposed)
            return Task.CompletedTask;
        _disposed = true;
        CaptionRegistryDrain retired = Swap(_createEmpty());
        return Task.WhenAll(
            _retiredStates.Select(state => state.RetireAsync())
                .Append(retired.All.AsTask())
                .Distinct());
    }

    private CaptionRegistryDrain Swap(TState next)
    {
        TState previous;
        lock (StateGate)
        {
            previous = _current;
            _current = next;
        }

        try
        {
            _publish(next);
        }
        catch
        {
            // Metadata observers cannot roll back a committed capability transition.
        }

        Task retired = previous.RetireAndReleasePayloadAsync();
        if (!retired.IsCompleted)
        {
            _retiredStates.Add(previous);
            _ = retired.ContinueWith(
                _ => RemoveRetiredState(previous),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return new CaptionRegistryDrain(retired, previous.DrainOwnerAsync);
    }

    private void RemoveRetiredState(TState state)
    {
        lock (_mutationGate)
            _retiredStates.Remove(state);
    }
}
