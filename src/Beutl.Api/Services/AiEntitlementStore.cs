using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Reactive.Bindings;

namespace Beutl.Api.Services;

internal sealed class AiEntitlementStore : IBeutlApiResource, IDisposable
{
    private readonly ReactivePropertySlim<AiEntitlements?> _entitlements = new();
    private readonly ReadOnlyReactivePropertySlim<AiEntitlements?> _readOnlyEntitlements;
    private readonly IDisposable _authenticationSubscription;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _balanceRequestGate = new(1, 1);
    private AuthenticatedUser? _owner;
    private long _lastAppliedSnapshotRequest;
    private long _nextSnapshotRequest;
    private bool _disposed;

    public AiEntitlementStore(BeutlApiApplication application)
    {
        _readOnlyEntitlements = _entitlements.ToReadOnlyReactivePropertySlim();
        _authenticationSubscription = application.AuthenticatedUser.Subscribe(HandleAuthenticatedUserChanged);
    }

    public IReadOnlyReactiveProperty<AiEntitlements?> Entitlements => _readOnlyEntitlements;

    public long BeginSnapshotRequest()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return ++_nextSnapshotRequest;
        }
    }

    public Task WaitForBalanceRequestAsync(CancellationToken cancellationToken)
        => _balanceRequestGate.WaitAsync(cancellationToken);

    public void ReleaseBalanceRequest()
        => _balanceRequestGate.Release();

    public void ApplyEntitlements(
        AiEntitlements? entitlements,
        AuthenticatedUser owner,
        long snapshotRequest)
    {
        lock (_gate)
        {
            if (_disposed
                || !ReferenceEquals(_owner, owner)
                || snapshotRequest < _lastAppliedSnapshotRequest)
            {
                return;
            }

            _lastAppliedSnapshotRequest = snapshotRequest;
            _entitlements.Value = entitlements;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        _authenticationSubscription.Dispose();
        _readOnlyEntitlements.Dispose();
        _entitlements.Dispose();
    }

    private void HandleAuthenticatedUserChanged(AuthenticatedUser? user)
    {
        lock (_gate)
        {
            if (_disposed || ReferenceEquals(_owner, user))
                return;

            _owner = user;
            _lastAppliedSnapshotRequest = ++_nextSnapshotRequest;
            _entitlements.Value = null;
        }
    }
}
