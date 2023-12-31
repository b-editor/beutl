using System.Diagnostics;
using System.Net.Http.Headers;

namespace Beutl.Api.Objects;

public class AuthorizedUser(Profile profile, AuthResponse response, BeutlApiApplication clients, HttpClient httpClient)
{
    public Profile Profile { get; } = profile;

    public string Token => response.Token;

    public string RefreshToken => response.Refresh_token;

    public DateTimeOffset Expiration => response.Expiration;

    public bool IsExpired => Expiration < DateTimeOffset.UtcNow;

    public MyAsyncLock Lock => clients.Lock;

    public async ValueTask RefreshAsync(bool force = false)
    {
        using Activity? activity = clients.ActivitySource.StartActivity("AuthorizedUser.Refresh", ActivityKind.Client);

        activity?.SetTag("force", force);
        activity?.SetTag("is_expired", IsExpired);

        if (force || IsExpired)
        {
            response = await clients.Account.RefreshAsync(new RefeshTokenRequest(RefreshToken, Token))
                .ConfigureAwait(false);
            activity?.AddEvent(new("Refreshed"));
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);

            if (clients.AuthorizedUser.Value == this)
            {
                clients.SaveUser();
                activity?.AddEvent(new("Saved"));
            }
        }
    }

    public async Task<StorageUsageResponse> StorageUsageAsync()
    {
        return await clients.Account.StorageUsageAsync();
    }
}
