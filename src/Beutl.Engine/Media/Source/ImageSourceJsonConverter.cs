using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.Media.Source;

public sealed class ImageSourceJsonConverter : JsonConverter<IImageSource?>
{
    public override IImageSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? s = reader.GetString();

        return s != null && BitmapSource.TryOpen(s, out var imageSource)
            ? imageSource
            : null;
    }

    public override void Write(Utf8JsonWriter writer, IImageSource? value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value?.Name);
    }
}
