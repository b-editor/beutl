using System.Text.Json.Serialization;

namespace Beutl.Api.Clients;

internal sealed class CreateAiImageRequest
{
    [JsonPropertyName("prompt")] public required string Prompt { get; init; }

    [JsonPropertyName("size")] public required string Size { get; init; }
}
