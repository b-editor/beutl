using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Reactive.Bindings;

namespace Beutl.Api.Services;

internal sealed class AiEntitlementStore : IBeutlApiResource, IDisposable
{
    private readonly ReactivePropertySlim<AiEntitlements?> _entitlements = new();
    private readonly SynchronizedReadOnlyReactiveProperty<AiEntitlements?> _readOnlyEntitlements;
    private readonly IDisposable _authenticationSubscription;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _balanceRequestGate = new(1, 1);
    private AuthenticatedUser? _owner;
    private bool _disposed;

    public AiEntitlementStore(BeutlApiApplication application)
    {
        _readOnlyEntitlements = new SynchronizedReadOnlyReactiveProperty<AiEntitlements?>(_entitlements);
        _authenticationSubscription = application.AuthenticatedUser.Subscribe(HandleAuthenticatedUserChanged);
    }

    public IReadOnlyReactiveProperty<AiEntitlements?> Entitlements => _readOnlyEntitlements;

    public Task WaitForBalanceRequestAsync(CancellationToken cancellationToken)
        => _balanceRequestGate.WaitAsync(cancellationToken);

    public void ReleaseBalanceRequest()
        => _balanceRequestGate.Release();

    public void ApplyEntitlements(
        AiEntitlements? entitlements,
        AuthenticatedUser owner)
    {
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(_owner, owner))
                return;

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
            _entitlements.Value = null;
        }
    }
}
