using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

public class RefreshTokenRequest
{
    [JsonPropertyName("token")] public required string Token { get; init; }

    [JsonPropertyName("refresh_token")] public required string RefreshToken { get; init; }
}
