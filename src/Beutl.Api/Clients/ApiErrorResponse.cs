using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

public class ApiErrorResponse
{
    [JsonPropertyName("error_code")] public required ApiErrorCode ErrorCode { get; init; }

    [JsonPropertyName("message")] public required string? Message { get; init; }

    [JsonPropertyName("documentation_url")]
    public required string? DocumentationUrl { get; init; }
}
