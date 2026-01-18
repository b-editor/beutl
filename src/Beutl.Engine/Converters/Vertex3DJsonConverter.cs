using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.Graphics3D.Meshes;

namespace Beutl.Converters;

internal sealed class Vertex3DJsonConverter : JsonConverter<Vertex3D>
{
    public override Vertex3D Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();
        if (s == null)
            throw new Exception("Invalid Vertex3D.");

        return Vertex3D.Parse(s);
    }

    public override void Write(Utf8JsonWriter writer, Vertex3D value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
