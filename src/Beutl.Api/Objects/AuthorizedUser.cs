using System.Net.Http.Headers;

namespace Beutl.Api.Objects;

public class AuthorizedUser
{
    private readonly BeutlClients _clients;
    private readonly HttpClient _httpClient;
    private AuthResponse _response;

    public AuthorizedUser(Profile profile, AuthResponse response, BeutlClients clients, HttpClient httpClient)
    {
        Profile = profile;
        _response = response;
        _clients = clients;
        _httpClient = httpClient;
    }

    public Profile Profile { get; }

    public string Token => _response.Token;

    public string RefreshToken => _response.Refresh_token;

    public DateTimeOffset Expiration => _response.Expiration;

    public bool IsExpired => Expiration < DateTimeOffset.UtcNow;

    public async ValueTask RefreshAsync(bool force = false)
    {
        if (force || IsExpired)
        {
            _response = await _clients.Account.RefreshAsync(new RefeshTokenRequest(RefreshToken, Token));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        }
    }
}
