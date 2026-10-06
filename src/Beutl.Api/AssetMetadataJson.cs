using System.Text.Json.Serialization;

namespace Beutl.Api;

public sealed class AssetMetadataJson
{
    [JsonPropertyName("id")] public required string Id { get; init; }

    [JsonPropertyName("os")] public required string OS { get; init; }

    [JsonPropertyName("arch")] public required string Arch { get; init; }

    [JsonPropertyName("version")] public required string Version { get; init; }

    [JsonPropertyName("standalone")] public required string Standalone { get; init; }

    // Metadata and server query values: zip,debian,installer,app,flatpak.
    [JsonPropertyName("type")] public required string Type { get; init; }
}
