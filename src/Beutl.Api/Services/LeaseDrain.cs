namespace Beutl.Api.Services;

/// <summary>
/// Counts the leases taken on one registration so its retirement can wait for the last release.
/// </summary>
/// <remarks>
/// Retiring refuses new leases at once; the returned task completes when the active count reaches zero.
/// </remarks>
internal sealed class LeaseDrain
{
    private readonly object _gate = new();
    private TaskCompletionSource? _drained;
    private int _activeLeases;
    private bool _retired;

    public bool TryAcquire()
    {
        lock (_gate)
        {
            if (_retired)
                return false;

            _activeLeases++;
            return true;
        }
    }

    public Task RetireAsync()
    {
        lock (_gate)
        {
            _retired = true;
            return _activeLeases == 0
                ? Task.CompletedTask
                : (_drained ??= new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;
        }
    }

    public void Release()
    {
        TaskCompletionSource? drained = null;
        lock (_gate)
        {
            _activeLeases--;
            if (_retired && _activeLeases == 0)
                drained = _drained;
        }

        drained?.TrySetResult();
    }
}
