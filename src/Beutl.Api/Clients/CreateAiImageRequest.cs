using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

internal sealed class CreateAiImageRequest
{
    [JsonPropertyName("prompt")] public required string Prompt { get; init; }

    // The endpoint also accepts the fixed sizes it shipped with, but exactly one
    // of the two may be sent. A ratio is what the provider actually speaks and
    // the only way to ask for 16:9 or a vertical image.
    [JsonPropertyName("aspectRatio")] public required string AspectRatio { get; init; }

    [JsonPropertyName("background")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Background { get; init; }

    [JsonPropertyName("seed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Seed { get; init; }

    // Omitted rather than sent empty: the endpoint runs the operation's default
    // model when no model is named, and refuses an id it does not know.
    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; init; }
}
