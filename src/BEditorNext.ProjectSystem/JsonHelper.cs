using System.Text.Json;

using BEditorNext.JsonConverters;

namespace BEditorNext;

internal class JsonHelper
{
    public static JsonWriterOptions WriterOptions { get; } = new()
    {
        Indented = true,
    };

    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = true,
        Converters =
        {
            new PixelPointConverter(),
            new PixelRectConverter(),
            new PixelSizeConverter(),
            new PointConverter(),
            new RectConverter(),
            new SizeConverter()
        }
    };
}
