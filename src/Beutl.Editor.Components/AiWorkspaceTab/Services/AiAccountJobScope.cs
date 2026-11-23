using System.Reactive.Disposables;
using Beutl.Api.Services;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.AiWorkspaceTab.Services;

/// <summary>Keeps a multi-step AI job on the authenticated session that started it.</summary>
internal sealed class AiAccountJobScope : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<AiAccountJobScope>();
    private readonly AiRequestRecoveryContext? _context;
    private readonly AiAuthenticatedRequestIdentity? _identity;
    private readonly CancellationTokenSource _identityCancellation = new();
    private readonly Lock _gate = new();
    private Task? _cancelling;
    private bool _disposed;

    public AiAccountJobScope(AiRequestRecoveryContext? context)
    {
        _context = context;
        _identity = context?.GetRequiredIdentity();
        if (context is not null)
        {
            context.IdentityChanged += OnIdentityChanged;
            OnIdentityChanged();
        }
    }

    public bool AccountChanged => _context is not null
        && (_identityCancellation.IsCancellationRequested || _context.GetCurrentIdentity() != _identity);

    public CancellationTokenSource Link(CancellationToken cancellationToken)
        => CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _identityCancellation.Token);

    public void ThrowIfInvalid(CancellationToken cancellationToken)
    {
        if (AccountChanged)
            throw new AuthenticationRequiredException(currentAttemptReservationIsKnownAbsent: false);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public IDisposable Enter(CancellationToken cancellationToken)
    {
        ThrowIfInvalid(cancellationToken);
        return _identity is { } identity ? AiAuthenticatedRequestScope.Enter(identity) : Disposable.Empty;
    }

    public async Task<T> SendAsync<T>(Func<CancellationToken, Task<T>> send, CancellationToken cancellationToken)
    {
        using IDisposable scope = Enter(cancellationToken);
        return await send(cancellationToken).ConfigureAwait(false);
    }

    private void OnIdentityChanged()
    {
        if (_context?.GetCurrentIdentity() == _identity)
            return;

        lock (_gate)
        {
            // Authentication changes are published under the API's authentication lock.
            // Mark cancellation immediately and run callbacks outside that lock.
            if (!_disposed)
                _cancelling ??= _identityCancellation.CancelAsync();
        }
    }

    public void Dispose()
    {
        if (_context is not null)
            _context.IdentityChanged -= OnIdentityChanged;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_cancelling is null)
                _identityCancellation.Dispose();
            else
                _ = DisposeAfterCancellationAsync(_cancelling);
        }
    }

    private async Task DisposeAfterCancellationAsync(Task cancelling)
    {
        try
        {
            await cancelling.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "An AI job's account change cancellation callback failed.");
        }
        finally
        {
            _identityCancellation.Dispose();
        }
    }
}
