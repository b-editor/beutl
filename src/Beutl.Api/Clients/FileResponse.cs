using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

public sealed class FileResponse
{
    [JsonPropertyName("id")] public required string Id { get; init; }

    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("contentType")] public required string ContentType { get; init; }

    [JsonPropertyName("downloadUrl")] public required string DownloadUrl { get; init; }

    [JsonPropertyName("size")] public required long Size { get; init; }

    [JsonPropertyName("sha256")] public required string? Sha256 { get; init; }
}
