using System.Diagnostics;
using System.Net.Http.Headers;

using Beutl.Api.Services;

namespace Beutl.Api.Objects;

public class AuthorizedUser(
    Profile profile, AuthResponse response,
    BeutlApiApplication clients, HttpClient httpClient, DateTime writeTime)
{
    private AuthResponse _response = response;
    // user.jsonに書き込まれた時間
    internal DateTime _writeTime = writeTime;

    public Profile Profile { get; } = profile;

    public string Token => _response.Token;

    public string RefreshToken => _response.Refresh_token;

    public DateTimeOffset Expiration => _response.Expiration;

    public bool IsExpired => Expiration < DateTimeOffset.UtcNow;

    public MyAsyncLock Lock => clients.Lock;

    public async ValueTask RefreshAsync(bool force = false)
    {
        using Activity? activity = clients.ActivitySource.StartActivity("AuthorizedUser.Refresh", ActivityKind.Client);

        string fileName = Path.Combine(Helper.AppRoot, "user.json");
        if (File.Exists(fileName))
        {
            DateTime lastWriteTime = File.GetLastWriteTimeUtc(fileName);
            if (_writeTime < lastWriteTime)
            {
                AuthorizedUser? fileUser = await clients.ReadUserAsync();
                if (fileUser?.Profile?.Id == Profile.Id)
                {
                    _response = fileUser._response;
                    _writeTime = lastWriteTime;
                }
                else if (fileUser != null)
                {
                    clients.SignOut(false);
                    throw new BeutlApiException<ApiErrorResponse>(
                        message: "The user may have been changed in another process.",
                        statusCode: 401,
                        response: "",
                        headers: new Dictionary<string, IEnumerable<string>>(),
                        result: new ApiErrorResponse(
                            documentation_url: "",
                            error_code: ApiErrorCode.Unknown,
                            message: "The user may have been changed in another process."),
                        innerException: null);
                }
            }
        }

        activity?.SetTag("force", force);
        activity?.SetTag("is_expired", IsExpired);

        if (force || IsExpired)
        {
            _response = await clients.Account.RefreshAsync(new RefeshTokenRequest(RefreshToken, Token))
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
