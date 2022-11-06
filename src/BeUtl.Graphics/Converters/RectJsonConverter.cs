using System.Text.Json;
using System.Text.Json.Serialization;

using Beutl.Graphics;

namespace Beutl.Converters;

internal sealed class RectJsonConverter : JsonConverter<Rect>
{
    public override Rect Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid Rect.");

        return Rect.Parse(s);
    }

    public override void Write(Utf8JsonWriter writer, Rect value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
