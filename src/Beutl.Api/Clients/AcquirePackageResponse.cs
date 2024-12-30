using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

public class AcquirePackageResponse
{
    [JsonPropertyName("package")] public required SimplePackageResponse Package { get; init; }

    [JsonPropertyName("latestRelease")] public required ReleaseResponse? LatestRelease { get; init; }
}
