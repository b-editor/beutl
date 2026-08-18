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

    [JsonPropertyName("aspectRatio")] public string AspectRatio { get; init; } = "16:9";

    [JsonPropertyName("generateAudio")] public bool GenerateAudio { get; init; } = true;

    [JsonPropertyName("seed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Seed { get; init; }
}
