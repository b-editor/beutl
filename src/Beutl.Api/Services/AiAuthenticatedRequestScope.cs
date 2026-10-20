using Beutl.Api.Objects;

namespace Beutl.Api.Services;

internal readonly record struct AiAuthenticatedRequestIdentity(
    string AccountId,
    AuthenticatedUser? User);

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
