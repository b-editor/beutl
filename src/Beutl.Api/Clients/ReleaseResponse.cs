using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

public sealed class ReleaseResponse
{
    [JsonPropertyName("id")] public required string Id { get; init; }

    [JsonPropertyName("version")] public required string Version { get; init; }

    [JsonPropertyName("title")] public required string Title { get; init; }

    [JsonPropertyName("description")] public required string Description { get; init; }

    [JsonPropertyName("targetVersion")] public required string? TargetVersion { get; init; }

    [JsonPropertyName("fileId")] public required string? FileId { get; init; }

    [JsonPropertyName("fileUrl")] public required string? FileUrl { get; init; }
}
