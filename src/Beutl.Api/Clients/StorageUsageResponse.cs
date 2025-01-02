using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

public class StorageUsageResponse
{
    [JsonPropertyName("size")] public required long Size { get; init; }

    [JsonPropertyName("max_size")] public required long MaxSize { get; init; }

    [JsonPropertyName("details")] public required Dictionary<string, long> Details { get; init; }
}
