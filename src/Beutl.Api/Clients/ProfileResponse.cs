using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

public sealed class ProfileResponse
{
    [JsonPropertyName("id")] public required string Id { get; init; }

    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("displayName")] public required string DisplayName { get; init; }

    [JsonPropertyName("bio")] public required string? Bio { get; init; }

    [JsonPropertyName("iconId")] public required string? IconId { get; init; }

    [JsonPropertyName("iconUrl")] public required string? IconUrl { get; init; }
}
