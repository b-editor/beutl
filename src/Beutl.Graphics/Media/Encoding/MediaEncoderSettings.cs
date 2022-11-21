namespace Beutl.Media.Encoding;

public abstract record MediaEncoderSettings
{
    public Dictionary<string, object> CodecOptions { get; init; } = new();
}
