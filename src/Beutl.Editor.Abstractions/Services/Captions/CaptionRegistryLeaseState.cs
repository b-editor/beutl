namespace Beutl.Editor.Services.Captions;

internal abstract class CaptionRegistryLeaseState
{
    private readonly ManualResetEventSlim _drained = new(initialState: true);
    private readonly object _gate = new();
    private int _leaseCount;
    private bool _retired;

    public IDisposable AcquireLease()
    {
        lock (_gate)
        {
            if (_retired)
                throw new InvalidOperationException("The caption registry state has been retired.");

            if (_leaseCount++ == 0)
                _drained.Reset();

            return new Lease(this);
        }
    }

    public void Retire()
    {
        lock (_gate)
        {
            _retired = true;
        }
    }

    public void WaitForLeases()
    {
        lock (_gate)
        {
            if (_leaseCount == 0)
                return;
        }

        _drained.Wait();
    }

    private void Release()
    {
        lock (_gate)
        {
            if (--_leaseCount == 0)
                _drained.Set();
        }
    }

    private sealed class Lease(CaptionRegistryLeaseState owner) : IDisposable
    {
        private CaptionRegistryLeaseState? _owner = owner;

        public void Dispose()
            => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
