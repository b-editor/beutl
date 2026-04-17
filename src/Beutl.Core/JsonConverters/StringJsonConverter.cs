using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.JsonConverters;

internal abstract class StringJsonConverter<T> : JsonConverter<T>
{
    protected abstract string TypeName { get; }

    protected abstract T Parse(string s);

    protected virtual string Format(T value) => value!.ToString()!;

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        return s != null ? Parse(s) : throw new Exception($"Invalid {TypeName}.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(Format(value));
    }
}
