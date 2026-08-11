using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

internal sealed record AiJobHistoryResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("inputParams")]
    public JsonElement? InputParams { get; init; }

    [JsonPropertyName("fileId")]
    public string? FileId { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }


    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("canRetry")]
    public required bool CanRetry { get; init; }

    [JsonPropertyName("createdAt")]
    public required DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updatedAt")]
    public required DateTimeOffset UpdatedAt { get; init; }
}

internal sealed record AiJobHistoryPageResponse
{
    [JsonPropertyName("jobs")]
    public required AiJobHistoryResponse[] Jobs { get; init; }

    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; init; }
}

internal sealed record DeleteAiJobResponse
{
    [JsonPropertyName("deleted")]
    public required bool Deleted { get; init; }
}
