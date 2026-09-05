using Avalonia.Threading;
using Beutl.Api.Services;
using Reactive.Bindings;

namespace Beutl.Services.AI;

internal sealed class AiOperationAvailabilityTracker : IDisposable
{
    private readonly IAiOperationAvailabilityService _service;
    private readonly CancellationToken _lifetimeToken;
    private readonly TimeSpan _debounce;
    private readonly object _gate = new();
    private CancellationTokenSource? _requestCts;
    private long _revision;
    private bool _disposed;

    public AiOperationAvailabilityTracker(
        IAiOperationAvailabilityService service,
        CancellationToken lifetimeToken,
        TimeSpan? debounce = null)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _lifetimeToken = lifetimeToken;
        _debounce = debounce ?? TimeSpan.FromMilliseconds(250);
        if (_debounce < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(debounce));
    }

    /// <summary>
    /// What the server last said. A check that is pending, was never asked, or
    /// failed leaves this <see cref="AiOperationAvailabilityState.Unknown"/> —
    /// the caller must not read that as a refusal.
    /// </summary>
    public ReactivePropertySlim<AiOperationAvailabilityState> State { get; } =
        new(AiOperationAvailabilityState.Unknown);

    internal Task? CurrentCheck { get; private set; }

    public void Check(AiOperationAvailabilityRequest? request)
    {
        CancellationTokenSource? previous;
        CancellationTokenSource? current = null;
        long revision;
        lock (_gate)
        {
            if (_disposed)
                return;

            previous = _requestCts;
            revision = ++_revision;
            if (request is not null && !_lifetimeToken.IsCancellationRequested)
            {
                current = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeToken);
                _requestCts = current;
            }
            else
            {
                _requestCts = null;
            }
        }

        previous?.Cancel();
        previous?.Dispose();
        Publish(revision, AiOperationAvailabilityState.Unknown);
        CurrentCheck = current is null
            ? null
            : CheckCoreAsync(request!, revision, current);
    }

    public void Refresh(AiOperationAvailabilityRequest? request) => Check(request);

    public async Task<bool> CheckNowAsync(
        AiOperationAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await _service.CheckAsync(request, cancellationToken);
    }

    private async Task CheckCoreAsync(
        AiOperationAvailabilityRequest request,
        long revision,
        CancellationTokenSource requestCts)
    {
        AiOperationAvailabilityState state = AiOperationAvailabilityState.Unknown;
        try
        {
            if (_debounce > TimeSpan.Zero)
                await Task.Delay(_debounce, requestCts.Token);
            state = await _service.CheckAsync(request, requestCts.Token)
                ? AiOperationAvailabilityState.Available
                : AiOperationAvailabilityState.Unavailable;
        }
        catch (OperationCanceledException) when (requestCts.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            // A check that could not be made has not refused anything. The
            // authoritative check still runs before the paid request is sent.
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_requestCts, requestCts))
                    _requestCts = null;
            }

            requestCts.Dispose();
        }

        Publish(revision, state);
    }

    private void Publish(long revision, AiOperationAvailabilityState state)
    {
        void Apply()
        {
            lock (_gate)
            {
                if (_disposed || revision != _revision)
                    return;
                State.Value = state;
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
            Apply();
        else
            Dispatcher.UIThread.Post(Apply);
    }

    public void Dispose()
    {
        CancellationTokenSource? source;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _revision++;
            source = _requestCts;
            _requestCts = null;
        }

        source?.Cancel();
        source?.Dispose();
        State.Dispose();
    }
}
