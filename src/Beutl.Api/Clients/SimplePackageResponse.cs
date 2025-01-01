using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

public sealed class SimplePackageResponse
{
    [JsonPropertyName("id")] public required string Id { get; init; }

    [JsonPropertyName("owner")] public required ProfileResponse Owner { get; init; }

    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("displayName")] public required string DisplayName { get; init; }

    [JsonPropertyName("shortDescription")] public required string ShortDescription { get; init; }

    [JsonPropertyName("tags")] public required string[] Tags { get; init; }

    [JsonPropertyName("logoId")] public required string? LogoId { get; init; }

    [JsonPropertyName("logoUrl")] public required string? LogoUrl { get; init; }

    [JsonPropertyName("currency")] public required string? Currency { get; init; }

    [JsonPropertyName("price")] public required int? Price { get; init; }

    [JsonPropertyName("paid")] public required bool Paid { get; init; }

    [JsonPropertyName("owned")] public required bool Owned { get; init; }
}
