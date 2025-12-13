using System.Text.Json;
using System.Text.Json.Serialization;

using Beutl.Media;

namespace Beutl.Converters;

internal sealed class GradingColorJsonConverter : JsonConverter<GradingColor>
{
    public override GradingColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string s = reader.GetString() ?? throw new Exception("Invalid GradingColor.");
        return GradingColor.Parse(s);
    }

    public override void Write(Utf8JsonWriter writer, GradingColor value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
