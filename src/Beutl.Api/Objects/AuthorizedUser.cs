using System.Diagnostics;
using System.Net.Http.Headers;

namespace Beutl.Api.Objects;

public class AuthorizedUser
{
    private readonly BeutlApiApplication _clients;
    private readonly HttpClient _httpClient;
    private AuthResponse _response;

    public AuthorizedUser(Profile profile, AuthResponse response, BeutlApiApplication clients, HttpClient httpClient)
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

    public MyAsyncLock Lock => _clients.Lock;

    public async ValueTask RefreshAsync(bool force = false)
    {
        using Activity? activity = _clients.ActivitySource.StartActivity("AuthorizedUser.Refresh", ActivityKind.Client);

        activity?.SetTag("force", force);
        activity?.SetTag("is_expired", IsExpired);

        if (force || IsExpired)
        {
            _response = await _clients.Account.RefreshAsync(new RefeshTokenRequest(RefreshToken, Token))
                .ConfigureAwait(false);
            activity?.AddEvent(new("Refreshed"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

            if (_clients.AuthorizedUser.Value == this)
            {
                _clients.SaveUser();
                activity?.AddEvent(new("Saved"));
            }
        }
    }

    public async Task<StorageUsageResponse> StorageUsageAsync()
    {
        return await _clients.Account.StorageUsageAsync();
    }
}
