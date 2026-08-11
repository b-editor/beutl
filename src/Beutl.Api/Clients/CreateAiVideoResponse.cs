using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

internal sealed record CreateAiVideoResponse
{
    [JsonPropertyName("jobId")] public required string JobId { get; init; }

    [JsonPropertyName("status")] public required string Status { get; init; }
}

internal sealed record CreateAiVideoRequest
{
    [JsonPropertyName("prompt")] public required string Prompt { get; init; }

    [JsonPropertyName("durationSeconds")] public required int DurationSeconds { get; init; }

    [JsonPropertyName("resolution")] public string Resolution { get; init; } = "720p";
}
