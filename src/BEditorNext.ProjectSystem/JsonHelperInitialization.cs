using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

using BEditorNext.JsonConverters;

namespace BEditorNext;

internal class JsonHelperInitialization
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void AddJsonConverter()
    {
        IList<JsonConverter> converters = JsonHelper.SerializerOptions.Converters;
        converters.Add(new PixelPointConverter());
        converters.Add(new PixelRectConverter());
        converters.Add(new PixelSizeConverter());
        converters.Add(new PointConverter());
        converters.Add(new RectConverter());
        converters.Add(new SizeConverter());
        converters.Add(new ColorConverter());
    }
}
