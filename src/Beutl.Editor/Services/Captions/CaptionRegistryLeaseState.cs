namespace Beutl.Editor.Services.Captions;

internal abstract class CaptionRegistryLeaseState
{
    private readonly object _gate = new();
    private TaskCompletionSource? _drained;
    private readonly Dictionary<object, int> _leases =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, TaskCompletionSource> _ownerDrains =
        new(ReferenceEqualityComparer.Instance);
    private int _leaseCount;
    private bool _retired;

    public IDisposable AcquireLease(IReadOnlyCollection<object> owners)
    {
        ArgumentNullException.ThrowIfNull(owners);
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        object[] snapshot = owners.Where(seen.Add).ToArray();
        lock (_gate)
        {
            if (_retired)
                throw new InvalidOperationException("The caption registry state has been retired.");

            foreach (object owner in snapshot)
            {
                _leases.TryGetValue(owner, out int count);
                _leases[owner] = count + 1;
            }
            _leaseCount++;

            return new Lease(this, snapshot);
        }
    }

    public Task DrainOwnerAsync(object owner)
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

    private TaskCompletionSource AddOwnerDrain(object owner)
    {
        var drain = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _ownerDrains.Add(owner, drain);
        return drain;
    }

    private void Release(IReadOnlyList<object> owners)
    {
        TaskCompletionSource? drained = null;
        List<TaskCompletionSource>? ownerDrains = null;
        lock (_gate)
        {
            _leaseCount--;
            foreach (object owner in owners)
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
        CaptionRegistryLeaseState owner,
        IReadOnlyList<object> owners) : IDisposable
    {
        private CaptionRegistryLeaseState? _owner = owner;

        public void Dispose()
            => Interlocked.Exchange(ref _owner, null)?.Release(owners);
    }
}

internal sealed class CaptionRegistryDrain(
    Task all,
    Func<object, Task> drainOwner)
{
    public ValueTask All => new(all);

    public Task DrainOwnerAsync(object owner) => drainOwner(owner);
}
