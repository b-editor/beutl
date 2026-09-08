using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

internal sealed record AiImageResponse
{
    [JsonPropertyName("jobId")] public string? JobId { get; init; }

    [JsonPropertyName("fileId")] public required string FileId { get; init; }

    [JsonPropertyName("url")] public required string Url { get; init; }

    [JsonPropertyName("fileName")] public string? FileName { get; init; }

    [JsonPropertyName("contentType")] public string? ContentType { get; init; }
}
