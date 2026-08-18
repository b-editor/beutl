using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

internal sealed record AiCaptionTranslationSegmentContextDto
{
    [JsonPropertyName("groupId")]
    public required string GroupId { get; init; }

    [JsonPropertyName("partIndex")]
    public required int PartIndex { get; init; }

    [JsonPropertyName("start")]
    public required double Start { get; init; }

    [JsonPropertyName("end")]
    public required double End { get; init; }
}

internal sealed record AiCaptionTranslationSegmentDto
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("context")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiCaptionTranslationSegmentContextDto? Context { get; init; }
}

internal sealed record AiCaptionTranslationStyleDto
{
    [JsonPropertyName("glossary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Glossary { get; init; }

    [JsonPropertyName("maxCharactersPerLine")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxCharactersPerLine { get; init; }

    [JsonPropertyName("maxLines")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxLines { get; init; }
}

internal sealed record AiCaptionTranslationRequestDto
{
    [JsonPropertyName("sourceLanguage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceLanguage { get; init; }

    [JsonPropertyName("targetLanguage")]
    public required string TargetLanguage { get; init; }

    [JsonPropertyName("segments")]
    public required AiCaptionTranslationSegmentDto[] Segments { get; init; }

    [JsonPropertyName("style")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiCaptionTranslationStyleDto? Style { get; init; }
}

internal sealed record AiCaptionTranslationResponseDto
{
    [JsonPropertyName("jobId")]
    public string? JobId { get; init; }

    [JsonPropertyName("segments")]
    public required AiCaptionTranslationSegmentDto[] Segments { get; init; }
}
