namespace Beutl.Graphics.Rendering.Requests;

internal readonly record struct RenderCacheFormatIdentity(
    string PixelFormat,
    string AlphaType,
    string ColorSpace)
{
    public static RenderCacheFormatIdentity LinearPremultipliedRgba16Float { get; } =
        new("RGBA16Float", "Premultiplied", "LinearSrgb");

    public void ThrowIfUninitialized(string parameterName)
    {
        if (string.IsNullOrWhiteSpace(PixelFormat)
            || string.IsNullOrWhiteSpace(AlphaType)
            || string.IsNullOrWhiteSpace(ColorSpace))
        {
            throw new ArgumentException(
                "A render-cache format identity must name its pixel, alpha, and color-space contracts.",
                parameterName);
        }
    }
}
