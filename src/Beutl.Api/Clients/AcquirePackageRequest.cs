using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

public class AcquirePackageRequest
{
    [JsonPropertyName("packageId")] public required string PackageId { get; init; }
}
