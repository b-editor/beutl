using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

// Only the model list is read. The endpoint also publishes the shapes, limits
// and formats each operation accepts, but this client holds its own copies of
// those; the models are the one thing it cannot know without asking, because an
// administrator registers them at runtime.
internal sealed record AiModelDescriptionResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    // "low" / "medium" / "high", or absent when the operation offers a single
    // model. Never a price: the server does not publish one.
    [JsonPropertyName("costTier")]
    public string? CostTier { get; init; }

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; init; }
}

internal sealed record AiOperationCapabilityResponse
{
    [JsonPropertyName("models")]
    public ImmutableArray<AiModelDescriptionResponse>? Models { get; init; }
}

internal sealed record AiCapabilitiesResponse
{
    [JsonPropertyName("operations")]
    public required ImmutableDictionary<string, AiOperationCapabilityResponse> Operations { get; init; }
}
