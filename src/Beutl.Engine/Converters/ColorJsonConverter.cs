using System.Text.Json;
using System.Text.Json.Serialization;

using Beutl.Media;

namespace Beutl.Converters;

internal sealed class ColorJsonConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string s = reader.GetString() ?? throw new Exception("Invalid Color.");
        return Color.Parse(s);
    }

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
