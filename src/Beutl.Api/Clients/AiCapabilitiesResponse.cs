using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

// What each model offers, which this client cannot know without asking: an
// administrator registers the models at runtime, and what a video request may
// carry differs per model — one renders only at 2K, another takes any whole
// second from 4 to 30. The operation-level shapes and limits published beside
// them are the outer bounds, and this client keeps its own copies of those.
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

    // Image models only, and absent for a server that predates them.
    [JsonPropertyName("aspectRatios")]
    public ImmutableArray<string>? AspectRatios { get; init; }

    // The backgrounds the model publishes rather than a single "can it cut one
    // out": GPT Image-1 offers auto, opaque and transparent while GPT Image-2
    // offers auto and opaque, and a boolean could not tell the two apart.
    [JsonPropertyName("backgrounds")]
    public ImmutableArray<string>? Backgrounds { get; init; }

    [JsonPropertyName("maxReferenceImages")]
    public int? MaxReferenceImages { get; init; }

    // Whether a size can be asked for, which is what upscaling is. Absent from
    // a server that predates it, which reads as "no restriction published".
    [JsonPropertyName("resolution")]
    public bool? Resolution { get; init; }

    // Video models only. Absent for every other operation, and absent from a
    // server that predates them, which reads as "no restriction published".
    [JsonPropertyName("durationsSeconds")]
    public ImmutableArray<int>? DurationsSeconds { get; init; }

    [JsonPropertyName("resolutions")]
    public ImmutableArray<string>? Resolutions { get; init; }

    [JsonPropertyName("audio")]
    public bool? Audio { get; init; }

    [JsonPropertyName("seed")]
    public bool? Seed { get; init; }

    // Video models only, and separately: a model that conditions on a first
    // frame does not necessarily take a last one.
    [JsonPropertyName("firstFrame")]
    public bool? FirstFrame { get; init; }

    [JsonPropertyName("lastFrame")]
    public bool? LastFrame { get; init; }
}

internal sealed record AiOperationCapabilityResponse
{
    [JsonPropertyName("models")]
    public ImmutableArray<AiModelDescriptionResponse>? Models { get; init; }

    // Video only, and the outer bounds rather than a menu: what the server will
    // accept at all, whatever model a request names. A model's own list is
    // narrowed to these, so a shape the server would refuse is never offered.
    [JsonPropertyName("resolutions")]
    public ImmutableArray<string>? Resolutions { get; init; }

    [JsonPropertyName("aspectRatios")]
    public ImmutableArray<string>? AspectRatios { get; init; }

    // Image only, and the outer bound in the same way: what the server takes at
    // all, whatever model a request names.
    [JsonPropertyName("backgrounds")]
    public ImmutableArray<string>? Backgrounds { get; init; }

    [JsonPropertyName("minDurationSeconds")]
    public int? MinDurationSeconds { get; init; }

    [JsonPropertyName("maxDurationSeconds")]
    public int? MaxDurationSeconds { get; init; }
}

internal sealed record AiCapabilitiesResponse
{
    [JsonPropertyName("operations")]
    public required ImmutableDictionary<string, AiOperationCapabilityResponse> Operations { get; init; }
}
