using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
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
