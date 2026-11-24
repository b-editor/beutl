namespace Beutl.Media.Decoding;

[Flags]
public enum DecoderCapabilities
{
    None = 0,
    DeinterlaceAuto = 1 << 0,
    DeinterlaceExplicit = 1 << 1,
    DeinterlaceFieldOrderOverride = 1 << 2,
    DeinterlaceDoubleRate = 1 << 3,
    DeinterlaceYadif = 1 << 4,
    DeinterlaceBwdif = 1 << 5,
}

public static class DecoderCapabilitiesExtensions
{
    public static bool Supports(this DecoderCapabilities capabilities, VideoDeinterlaceOptions options)
    {
        if (options.Mode == DeinterlaceMode.Off) return true;

        var required = options.Mode == DeinterlaceMode.Auto
            ? DecoderCapabilities.DeinterlaceAuto
            : DecoderCapabilities.DeinterlaceExplicit;
        required |= options.Method == DeinterlaceMethod.Yadif
            ? DecoderCapabilities.DeinterlaceYadif
            : DecoderCapabilities.DeinterlaceBwdif;
        if (options.FieldOrder != DeinterlaceFieldOrder.Auto)
            required |= DecoderCapabilities.DeinterlaceFieldOrderOverride;
        if (options.FrameRateMode == DeinterlaceFrameRateMode.Field)
            required |= DecoderCapabilities.DeinterlaceDoubleRate;

        return (capabilities & required) == required;
    }
}
