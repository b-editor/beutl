using Beutl.Media.Decoding;
using Beutl.Media.Wave;

namespace Beutl.Services.PrimitiveImpls;

public sealed class AnimatedPngReaderExtension : DecodingExtension
{
    public static readonly AnimatedPngReaderExtension Instance = new();
    private static readonly AnimatedPngDecoderInfo s_info = new();

    public override string Name => s_info.Name;

    public override string DisplayName => s_info.Name;

    public override IDecoderInfo GetDecoderInfo()
    {
        return s_info;
    }
}
