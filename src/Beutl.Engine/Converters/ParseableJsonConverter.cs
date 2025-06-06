using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.Converters;

/// <summary>
/// Generic JSON converter for types that implement IParsable.
/// This replaces the need for individual converters for each primitive type.
/// </summary>
/// <typeparam name="T">The type to convert</typeparam>
internal class ParseableJsonConverter<T> : JsonConverter<T> 
    where T : IParsable<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (s is null)
            throw new JsonException($"Invalid {typeof(T).Name}.");

        if (!T.TryParse(s, CultureInfo.InvariantCulture, out T? result))
            throw new JsonException($"Unable to parse '{s}' as {typeof(T).Name}.");

        return result;
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value?.ToString());
    }
}