using Beutl.Api.Objects;

namespace Beutl.Api.Services;

internal readonly record struct AuthenticatedApiResult<T>(
    T Value,
    AuthenticatedUser User);

internal sealed class AuthenticatedSessionContext(
    AuthenticatedUser user,
    long generation,
    string authorization,
    CancellationToken authenticationToken,
    CancellationToken applicationToken,
    CancellationTokenSource linkedCancellation) : IDisposable
{
    public AuthenticatedUser User { get; } = user;

    public long Generation { get; } = generation;

    public string Authorization { get; } = authorization;

    public CancellationToken AuthenticationToken { get; } = authenticationToken;

    public CancellationToken ApplicationToken { get; } = applicationToken;

    public CancellationToken CancellationToken => linkedCancellation.Token;

    public void Dispose()
    {
        linkedCancellation.Dispose();
    }
}
