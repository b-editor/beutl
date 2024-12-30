using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

public class CreateAuthUriRequest
{
    [JsonPropertyName("continue_uri")] public required string ContinueUri { get; init; }
}
