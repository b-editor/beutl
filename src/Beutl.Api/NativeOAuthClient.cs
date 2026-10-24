using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Beutl.Api.Clients;
using Refit;

namespace Beutl.Api;

internal sealed record NativeAuthorization(Uri Uri, string RedirectUri, string State, string Verifier, string PrivateKey);

internal static class NativeOAuthClient
{
    internal const string ClientId = "beutl-desktop";

    public static NativeAuthorization CreateAuthorization(string origin, string redirectUri)
    {
        string state = NativeProofKey.Encode(RandomNumberGenerator.GetBytes(32));
        string verifier = NativeProofKey.Encode(RandomNumberGenerator.GetBytes(32));
        string privateKey = NativeProofKey.Create();
        string name = new(Environment.MachineName.Where(c => !char.IsControl(c)).Take(80).ToArray());
        var query = new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri,
            ["scope"] = "beutl:desktop offline_access",
            ["resource"] = $"{origin}/api",
            ["prompt"] = "consent",
            ["state"] = state,
            ["code_challenge"] = NativeProofKey.Hash(verifier),
            ["code_challenge_method"] = "S256",
            ["dpop_jkt"] = NativeProofKey.Thumbprint(privateKey),
            ["device_name"] = string.IsNullOrWhiteSpace(name) ? "Desktop" : name,
        };
        string uri = $"{origin}/api/auth/oauth2/authorize?" + string.Join('&', query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new NativeAuthorization(new Uri(uri), redirectUri, state, verifier, privateKey);
    }

    public static Task<AuthResponse> ExchangeAsync(HttpClient client, NativeAuthorization authorization, string code, CancellationToken ct)
        => TokenAsync(client, authorization.PrivateKey, new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = ClientId,
            ["redirect_uri"] = authorization.RedirectUri,
            ["code"] = code,
            ["code_verifier"] = authorization.Verifier,
        }, ct);

    public static Task<AuthResponse> RefreshAsync(HttpClient client, AuthResponse response, CancellationToken ct)
        => TokenAsync(client, response.DpopPrivateKey ?? throw new InvalidOperationException("Missing device key."),
            new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["client_id"] = ClientId, ["refresh_token"] = response.RefreshToken }, ct);

    private static async Task<AuthResponse> TokenAsync(HttpClient client, string privateKey, Dictionary<string, string> form, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/oauth2/token") { Content = new FormUrlEncodedContent(form) };
        request.Options.Set(NativeProofHandler.ProofKey, privateKey);
        request.Options.Set(NativeProofHandler.TokenRequest, true);
        using HttpResponseMessage response = await client.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.BadRequest && form["grant_type"] == "refresh_token"
            && await IsInvalidGrantAsync(response, ct).ConfigureAwait(false))
        {
            // Reuse the startup recovery that removes expired saved credentials and offers sign-in.
            using var invalidRefresh = new HttpResponseMessage(response.StatusCode)
            {
                Content = JsonContent.Create(new ApiErrorResponse
                {
                    ErrorCode = ApiErrorCode.InvalidRefreshToken,
                    Message = "The device authorization has expired or was revoked.",
                    DocumentationUrl = null,
                }),
            };
            throw await ApiException.Create(request, request.Method, invalidRefresh, new RefitSettings()).ConfigureAwait(false);
        }
        response.EnsureSuccessStatusCode();
        using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
        JsonElement root = json.RootElement;
        string? accessToken = root.GetProperty("access_token").GetString();
        string? refreshToken = root.GetProperty("refresh_token").GetString();
        double expiresIn = root.GetProperty("expires_in").GetDouble();
        if (root.GetProperty("token_type").GetString() != "DPoP" || string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(refreshToken) || !double.IsFinite(expiresIn) || expiresIn <= 0 || expiresIn > 86_400)
            throw new InvalidOperationException("Invalid device authorization response.");
        return new AuthResponse
        {
            Token = accessToken,
            RefreshToken = refreshToken,
            Expiration = DateTime.UtcNow.AddSeconds(Math.Max(0, expiresIn - 30)),
            DpopPrivateKey = privateKey
        };
    }

    private static async Task<bool> IsInvalidGrantAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using JsonDocument json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), cancellationToken: ct).ConfigureAwait(false);
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("error", out JsonElement error)
                && error.ValueKind == JsonValueKind.String
                && error.GetString() == "invalid_grant";
        }
        catch (JsonException)
        {
            // An unreadable server error is not evidence that saved credentials are invalid.
            return false;
        }
    }

    public static async Task<ProfileResponse> GetProfileAsync(HttpClient client, AuthResponse authentication, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v3/user");
        request.Headers.Authorization = new AuthenticationHeaderValue("DPoP", authentication.Token);
        request.Options.Set(NativeProofHandler.ProofKey, authentication.DpopPrivateKey!);
        using HttpResponseMessage response = await client.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ProfileResponse>(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Missing profile response.");
    }
}
