using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

internal sealed record AiFixedOperationAvailabilityRequestDto
{
    [JsonPropertyName("operation")]
    public required string Operation { get; init; }
}

internal sealed record AiVideoOperationAvailabilityRequestDto
{
    [JsonPropertyName("operation")]
    public required string Operation { get; init; }

    [JsonPropertyName("durationSeconds")]
    public required int DurationSeconds { get; init; }
}

internal sealed record AiTranscriptionOperationAvailabilityRequestDto
{
    [JsonPropertyName("operation")]
    public required string Operation { get; init; }

    [JsonPropertyName("durationSeconds")]
    public required double DurationSeconds { get; init; }
}

internal sealed record AiTranslationOperationAvailabilityRequestDto
{
    [JsonPropertyName("operation")]
    public required string Operation { get; init; }

    [JsonPropertyName("characterCount")]
    public required int CharacterCount { get; init; }
}

internal sealed record AiOperationAvailabilityResponse
{
    [JsonPropertyName("available")]
    public required bool Available { get; init; }
}
