using System.Text.Json;

namespace BEditorNext;

public static class JsonHelper
{
    public static JsonWriterOptions WriterOptions { get; } = new()
    {
        Indented = true,
    };

    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        WriteIndented = true,
    };
}
