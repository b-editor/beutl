using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

internal sealed record AiVideoJobResponse
{
    [JsonPropertyName("jobId")] public required string JobId { get; init; }

    [JsonPropertyName("status")] public required string Status { get; init; }

    [JsonPropertyName("fileId")] public string? FileId { get; init; }

    [JsonPropertyName("url")] public string? Url { get; init; }

    [JsonPropertyName("error")] public string? Error { get; init; }
}
