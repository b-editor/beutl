using System.Text.Json.Nodes;

namespace Beutl.Media.Encoding;

public abstract class MediaEncoderSettings : CoreObject
{
    [Obsolete]
    public static readonly CoreProperty<JsonNode?> CodecOptionsProperty;

    [Obsolete]
    static MediaEncoderSettings()
    {
        CodecOptionsProperty = ConfigureProperty<JsonNode?, MediaEncoderSettings>(nameof(CodecOptions))
            .Register();
    }

    [Obsolete()]
    public JsonNode? CodecOptions
    {
        get => GetValue(CodecOptionsProperty);
        set => SetValue(CodecOptionsProperty, value);
    }
}
