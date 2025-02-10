using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

public sealed class CheckForUpdatesResponse
{
    [JsonPropertyName("latest_version")] public required string LatestVersion { get; init; }

    [JsonPropertyName("url")] public required string Url { get; init; }

    [JsonPropertyName("is_latest")] public required bool IsLatest { get; init; }

    [JsonPropertyName("must_latest")] public required bool MustLatest { get; init; }
}

public sealed class AppUpdateResponse
{
    [JsonPropertyName("latestVersion")] public required string LatestVersion { get; init; }

    [JsonPropertyName("url")] public required string? Url { get; init; }

    [JsonPropertyName("downloadUrl")] public required string? DownloadUrl { get; init; }

    [JsonPropertyName("isLatest")] public required bool IsLatest { get; init; }

    [JsonPropertyName("mustLatest")] public required bool MustLatest { get; init; }
}
