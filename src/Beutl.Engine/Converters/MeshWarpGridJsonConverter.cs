using System.Text.Json;
using System.Text.Json.Serialization;

using Beutl.Graphics;

namespace Beutl.Converters;

internal sealed class MeshWarpGridJsonConverter : JsonConverter<MeshWarpGrid>
{
    public override MeshWarpGrid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement mesh = document.RootElement;
        if (mesh.ValueKind != JsonValueKind.Object)
            throw new JsonException("A mesh warp grid must be an object.");

        int rows = ReadDimension(mesh, nameof(MeshWarpGrid.Rows));
        int columns = ReadDimension(mesh, nameof(MeshWarpGrid.Columns));
        IEnumerable<Point>? points = mesh.TryGetProperty(nameof(MeshWarpGrid.Points), out JsonElement array)
                                      && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Select(point => point.Deserialize<Point>(options))
            : null;
        return new MeshWarpGrid(rows, columns, points);
    }

    private static int ReadDimension(JsonElement mesh, string propertyName)
        => mesh.TryGetProperty(propertyName, out JsonElement dimension) && dimension.ValueKind == JsonValueKind.Number
            && dimension.TryGetInt32(out int value) ? value : MeshWarpGrid.DefaultCells;

    public override void Write(Utf8JsonWriter writer, MeshWarpGrid value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber(nameof(MeshWarpGrid.Rows), value.Rows);
        writer.WriteNumber(nameof(MeshWarpGrid.Columns), value.Columns);
        writer.WritePropertyName(nameof(MeshWarpGrid.Points));
        JsonSerializer.Serialize(writer, value.Points, options);
        writer.WriteEndObject();
    }
}
