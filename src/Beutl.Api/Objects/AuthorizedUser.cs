using Beutl.Api.Clients;

namespace Beutl.Api.Objects;

public class AuthenticatedUser(
    Profile profile,
    AuthResponse response,
    BeutlApiApplication clients,
    DateTime writeTime)
{
    private readonly object _stateGate = new();
    private AuthResponse _response = response;
    private DateTime _writeTime = writeTime;

    public Profile Profile { get; } = profile;

    public string Token
    {
        get
        {
            lock (_stateGate)
                return _response.Token;
        }
    }

    public string RefreshToken
    {
        get
        {
            lock (_stateGate)
                return _response.RefreshToken;
        }
    }

    public DateTimeOffset Expiration
    {
        get
        {
            lock (_stateGate)
                return _response.Expiration;
        }
    }

    public bool IsExpired => Expiration < DateTimeOffset.UtcNow;

    public MyAsyncLock Lock => clients.Lock;

    public ValueTask RefreshAsync(
        CancellationToken cancellationToken,
        bool force = false)
        => clients.RefreshAuthenticatedUserAsync(this, force, cancellationToken);

    internal (AuthResponse Response, DateTime WriteTime) GetAuthenticationState()
    {
        lock (_stateGate)
            return (_response, _writeTime);
    }

    internal void CommitAuthenticationState(AuthResponse nextResponse, DateTime nextWriteTime)
    {
        ArgumentNullException.ThrowIfNull(nextResponse);
        lock (_stateGate)
        {
            _response = nextResponse;
            _writeTime = nextWriteTime;
        }
    }

    internal void SetWriteTime(DateTime value)
    {
        lock (_stateGate)
            _writeTime = value;
    }
}
