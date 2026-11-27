using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.Agents;

internal static class AgentJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    internal static JsonElement ToElement<T>(T value) => JsonSerializer.SerializeToElement(value, Options);

    internal static T Read<T>(JsonElement? value) =>
        value is { } element && element.Deserialize<T>(Options) is { } result
            ? result
            : throw new JsonException($"Expected {typeof(T).Name}.");
}
