using Beutl.Api.Objects;

namespace Beutl.Api.Services;

internal readonly record struct AiAuthenticatedRequestIdentity(
    string AccountId,
    AuthenticatedUser? User);

internal static class AiAuthenticatedRequestScope
{
    private static readonly AsyncLocal<AiAuthenticatedRequestIdentity?> s_current = new();

    public static AiAuthenticatedRequestIdentity? Identity => s_current.Value;

    public static AuthenticatedUser? Current => Identity?.User;

    public static IDisposable Enter(AuthenticatedUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Enter(new AiAuthenticatedRequestIdentity(user.Profile.Id, user));
    }

    public static IDisposable Enter(AiAuthenticatedRequestIdentity identity)
    {
        AiAuthenticatedRequestIdentity? previous = s_current.Value;
        s_current.Value = identity;
        return new Scope(previous);
    }

    public static AuthenticatedUser? RequireExpectedUser(AuthenticatedUser? current)
    {
        if (Current is { } expected && !ReferenceEquals(current, expected))
            throw new AuthenticationRequiredException();
        return current;
    }

    private sealed class Scope(AiAuthenticatedRequestIdentity? previous) : IDisposable
    {
        private AiAuthenticatedRequestIdentity? _previous = previous;
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
