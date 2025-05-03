using Beutl.Media.Decoding;

namespace Beutl.Services.PrimitiveImpls;

public sealed class AnimatedImageReaderExtension : DecodingExtension
{
    public static readonly AnimatedImageReaderExtension Instance = new();
    private static readonly AnimatedImageDecoderInfo s_info = new();

    public override string Name => s_info.Name;

    public override string DisplayName => s_info.Name;

    public override IDecoderInfo GetDecoderInfo()
    {
        return s_info;
    }
}
