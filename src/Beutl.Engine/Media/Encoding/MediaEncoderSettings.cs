using System.Text.Json.Nodes;

namespace Beutl.Media.Encoding;

public abstract record MediaEncoderSettings
{
    public JsonNode? CodecOptions { get; init; }
}
