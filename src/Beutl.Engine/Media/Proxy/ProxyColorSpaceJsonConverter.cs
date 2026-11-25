using System.Text.Json;
using System.Text.Json.Serialization;

using SkiaSharp;

namespace Beutl.Media.Proxy;

internal sealed class ProxyColorSpaceJsonConverter : JsonConverter<BitmapColorSpace>
{
    public ProxyColorSpaceJsonConverter()
    {
    }

    private sealed record ColorSpaceData(BitmapColorSpaceTransferFn Transfer, float[] ToXyzD50);

    public override BitmapColorSpace? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var data = JsonSerializer.Deserialize<ColorSpaceData>(ref reader, options);
        if (data is not { ToXyzD50.Length: 9 })
            return null;

        var gamut = BitmapColorSpaceXyz.Create(data.ToXyzD50);
        var space = SKColorSpace.CreateRgb(data.Transfer.ToSKTransferFn(), gamut.ToSKXyz());
        return space is null ? null : BitmapColorSpace.FromSKColorSpace(space);
    }

    public override void Write(Utf8JsonWriter writer, BitmapColorSpace value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, new ColorSpaceData(
            value.GetNumericalTransferFunction(), value.ToColorSpaceXyz().Values.ToArray()), options);
}
