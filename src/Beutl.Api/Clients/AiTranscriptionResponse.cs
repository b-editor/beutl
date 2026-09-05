using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

internal sealed record AiTranscriptionResponseDto
{
    [JsonPropertyName("jobId")] public string? JobId { get; init; }

    [JsonPropertyName("segments")] public required AiTranscriptionSegmentDto[] Segments { get; init; }

    [JsonPropertyName("language")] public string? Language { get; init; }

    [JsonPropertyName("words")] public AiTranscriptionWordDto[]? Words { get; init; }
}

internal sealed class AiTranscriptionWordDto
{
    [JsonPropertyName("start")] public required double Start { get; init; }

    [JsonPropertyName("end")] public required double End { get; init; }

    [JsonPropertyName("word")] public required string Word { get; init; }
}

internal sealed class AiTranscriptionSegmentDto
{
    [JsonPropertyName("start")] public required double Start { get; init; }

    [JsonPropertyName("end")] public required double End { get; init; }

    [JsonPropertyName("text")] public required string Text { get; init; }
}
