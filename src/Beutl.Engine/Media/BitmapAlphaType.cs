using SkiaSharp;

namespace Beutl.Media;

public enum BitmapAlphaType
{
    Unknown,
    Opaque,
    Premul,
    Unpremul,
}

internal static class BitmapAlphaTypeExtensions
{
    public static SKAlphaType ToSKAlphaType(this BitmapAlphaType alphaType) => alphaType switch
    {
        BitmapAlphaType.Opaque => SKAlphaType.Opaque,
        BitmapAlphaType.Premul => SKAlphaType.Premul,
        BitmapAlphaType.Unpremul => SKAlphaType.Unpremul,
        _ => SKAlphaType.Unknown,
    };

    public static BitmapAlphaType FromSKAlphaType(SKAlphaType alphaType) => alphaType switch
    {
        SKAlphaType.Opaque => BitmapAlphaType.Opaque,
        SKAlphaType.Premul => BitmapAlphaType.Premul,
        SKAlphaType.Unpremul => BitmapAlphaType.Unpremul,
        _ => BitmapAlphaType.Unknown,
    };
}
