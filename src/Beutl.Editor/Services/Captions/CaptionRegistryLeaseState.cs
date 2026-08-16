namespace Beutl.Editor.Services.Captions;

internal abstract class CaptionRegistryLeaseState<TOwner>
    where TOwner : class
{
    private readonly object _gate = new();
    private TaskCompletionSource? _drained;
    private readonly Dictionary<TOwner, int> _leases =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<TOwner, TaskCompletionSource> _ownerDrains =
        new(ReferenceEqualityComparer.Instance);
    private int _leaseCount;
    private bool _retired;

    public IDisposable AcquireLease(IReadOnlyCollection<TOwner> owners)
    {
        ArgumentNullException.ThrowIfNull(owners);
        var seen = new HashSet<TOwner>(ReferenceEqualityComparer.Instance);
        TOwner[] snapshot = owners.Where(seen.Add).ToArray();
        lock (_gate)
        {
            if (_retired)
                throw new InvalidOperationException("The caption registry state has been retired.");

            foreach (TOwner owner in snapshot)
            {
                _leases.TryGetValue(owner, out int count);
                _leases[owner] = count + 1;
            }
            _leaseCount++;

            return new Lease(this, snapshot);
        }
    }

    public Task DrainOwnerAsync(TOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_gate)
        {
            if (!_leases.ContainsKey(owner))
                return Task.CompletedTask;

            return _ownerDrains.GetValueOrDefault(owner)?.Task
                ?? AddOwnerDrain(owner).Task;
        }
    }

    public Task RetireAsync()
    {
        lock (_gate)
        {
            _retired = true;
            return _leaseCount == 0
                ? Task.CompletedTask
                : (_drained ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
    }

    private TaskCompletionSource AddOwnerDrain(TOwner owner)
    {
        var drain = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _ownerDrains.Add(owner, drain);
        return drain;
    }

    private void Release(IReadOnlyList<TOwner> owners)
    {
        TaskCompletionSource? drained = null;
        List<TaskCompletionSource>? ownerDrains = null;
        lock (_gate)
        {
            _leaseCount--;
            foreach (TOwner owner in owners)
            {
                int count = _leases[owner] - 1;
                if (count == 0)
                {
                    _leases.Remove(owner);
                    if (_ownerDrains.Remove(owner, out TaskCompletionSource? ownerDrain))
                        (ownerDrains ??= []).Add(ownerDrain);
                }
                else
                {
                    _leases[owner] = count;
                }
            }

            if (_leaseCount == 0 && _retired)
            {
                drained = _drained;
            }
        }

        if (ownerDrains is not null)
        {
            foreach (TaskCompletionSource ownerDrain in ownerDrains)
                ownerDrain.TrySetResult();
        }
        drained?.TrySetResult();
    }

    private sealed class Lease(
        CaptionRegistryLeaseState<TOwner> owner,
        IReadOnlyList<TOwner> owners) : IDisposable
    {
        private CaptionRegistryLeaseState<TOwner>? _owner = owner;

        public void Dispose()
            => Interlocked.Exchange(ref _owner, null)?.Release(owners);
    }
}

internal sealed class CaptionRegistryDrain<TOwner>(
    Task all,
    Func<TOwner, Task> drainOwner)
    where TOwner : class
{
    public ValueTask All => new(all);

    public Task DrainOwnerAsync(TOwner owner) => drainOwner(owner);
}
