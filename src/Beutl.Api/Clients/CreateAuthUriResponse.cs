using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

public class CreateAuthUriResponse
{
    [JsonPropertyName("auth_uri")] public required string AuthUri { get; init; }

    [JsonPropertyName("session_id")] public required string SessionId { get; init; }
}
