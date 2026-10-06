using Beutl.Api.Objects;

namespace Beutl.Api.Services;

internal readonly record struct AiAuthenticatedRequestIdentity(
    string AccountId,
    AuthenticatedUser? User);

internal sealed class AiRetryAttemptContext(
    IAiRetryKeyStore store,
    Func<AiAuthenticatedRequestIdentity?> identityProvider,
    bool allowSyntheticIdentity = false) : IBeutlApiResource
{
    public IAiRetryKeyStore Store { get; } = store
        ?? throw new ArgumentNullException(nameof(store));

    private bool AllowSyntheticIdentity { get; } = allowSyntheticIdentity;

    public AiAuthenticatedRequestIdentity GetRequiredIdentity()
    {
        AiAuthenticatedRequestIdentity identity = identityProvider()
            ?? throw new AuthenticationRequiredException();
        if (string.IsNullOrWhiteSpace(identity.AccountId)
            || identity.User is null && !AllowSyntheticIdentity
            || identity.User is { } user
                && !StringComparer.Ordinal.Equals(user.Profile.Id, identity.AccountId))
        {
            throw new AuthenticationRequiredException();
        }
        return identity;
    }

    public IDisposable Enter(AiAuthenticatedRequestIdentity identity)
        => identity.User is null
            ? EmptyDisposable.Instance
            : AiAuthenticatedRequestScope.Enter(identity.User);
}

internal static class AiAuthenticatedRequestScope
{
    private static readonly AsyncLocal<AuthenticatedUser?> s_current = new();

    public static AuthenticatedUser? Current => s_current.Value;

    public static IDisposable Enter(AuthenticatedUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        AuthenticatedUser? previous = s_current.Value;
        s_current.Value = user;
        return new Scope(previous);
    }

    private sealed class Scope(AuthenticatedUser? previous) : IDisposable
    {
        private AuthenticatedUser? _previous = previous;
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                s_current.Value = _previous;
                _previous = null;
            }
        }
    }
}

internal sealed class EmptyDisposable : IDisposable
{
    public static EmptyDisposable Instance { get; } = new();

    public void Dispose()
    {
    }
}
