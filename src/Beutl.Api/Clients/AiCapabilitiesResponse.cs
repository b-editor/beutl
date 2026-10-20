using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

// Per-model capabilities are registered on the server at runtime, so they are asked for. The operation-level
// values published beside them are the outer bounds.
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

    [JsonPropertyName("promptToVideo")]
    public bool? PromptToVideo { get; init; }

    [JsonPropertyName("inputReferences")]
    public bool? InputReferences { get; init; }

    [JsonPropertyName("maxInputReferences")]
    public int? MaxInputReferences { get; init; }

    [JsonPropertyName("maxInputReferenceBytes")]
    public long? MaxInputReferenceBytes { get; init; }

    [JsonPropertyName("maxVideoReferences")]
    public int? MaxVideoReferences { get; init; }

    [JsonPropertyName("maxVideoReferenceBytes")]
    public long? MaxVideoReferenceBytes { get; init; }

    [JsonPropertyName("maxAudioReferences")]
    public int? MaxAudioReferences { get; init; }

    [JsonPropertyName("maxAudioReferenceBytes")]
    public long? MaxAudioReferenceBytes { get; init; }

    [JsonPropertyName("maxSourceVideoBytes")]
    public long? MaxSourceVideoBytes { get; init; }

    [JsonPropertyName("minSourceVideoSeconds")]
    public double? MinSourceVideoSeconds { get; init; }

    [JsonPropertyName("maxSourceVideoSeconds")]
    public double? MaxSourceVideoSeconds { get; init; }

    [JsonPropertyName("maxPromptLength")]
    public int? MaxPromptLength { get; init; }

    // Image models only, and absent for a server that predates them.
    [JsonPropertyName("aspectRatios")]
    public ImmutableArray<string>? AspectRatios { get; init; }

    // A list rather than a flag, since a boolean could not tell apart the backgrounds models offer.
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

    // Video only: the outer bounds for any model, to which a model's own list is narrowed.
    [JsonPropertyName("resolutions")]
    public ImmutableArray<string>? Resolutions { get; init; }

    [JsonPropertyName("aspectRatios")]
    public ImmutableArray<string>? AspectRatios { get; init; }

    // Image only, and the outer bound in the same way.
    [JsonPropertyName("backgrounds")]
    public ImmutableArray<string>? Backgrounds { get; init; }

    // Image generation only: the total for all reference pictures, not the per-picture limit times the count.
    [JsonPropertyName("maxReferenceImagesTotalBytes")]
    public long? MaxReferenceImagesTotalBytes { get; init; }

    [JsonPropertyName("maxSegments")]
    public int? MaxSegments { get; init; }

    [JsonPropertyName("maxCharacters")]
    public int? MaxCharacters { get; init; }

    [JsonPropertyName("maxRequestBytes")]
    public int? MaxRequestBytes { get; init; }

    [JsonPropertyName("minDurationSeconds")]
    public int? MinDurationSeconds { get; init; }

    [JsonPropertyName("maxDurationSeconds")]
    public int? MaxDurationSeconds { get; init; }

    // Image edits only: whether the task takes a prompt, which the server otherwise ignores.
    [JsonPropertyName("promptRequired")]
    public bool? PromptRequired { get; init; }

    // Motion control only.
    [JsonPropertyName("qualities")]
    public ImmutableArray<string>? Qualities { get; init; }
}

internal sealed record AiCapabilitiesResponse
{
    [JsonPropertyName("operations")]
    public required ImmutableDictionary<string, AiOperationCapabilityResponse> Operations { get; init; }
}
