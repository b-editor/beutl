using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Beutl.Api;

public partial class BeutlApiApplication
{
    public async Task<PreviewSessionResponse> CreatePreviewSessionAsync(CancellationToken cancellationToken)
    {
        var result = await SendAuthenticatedAsync(async (authorization, token) =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v3/preview/sessions");
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(authorization);
            using var response = await HttpClient.SendAsync(request, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<PreviewSessionResponse>(token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The preview service returned an empty session.");
        }, cancellationToken).ConfigureAwait(false);
        return result.Value;
    }
}

public sealed record PreviewSessionResponse(
    Uri ViewerUrl, Uri PublisherUrl, string PublisherToken, string PairingCode, DateTimeOffset CodeExpires,
    PreviewIceServer[]? IceServers = null);

public sealed record PreviewIceServer(
    [property: JsonPropertyName("urls")] string[] Urls,
    [property: JsonPropertyName("username")] string? Username = null,
    [property: JsonPropertyName("credential")] string? Credential = null);
