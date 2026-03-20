using SkiaSharp;

namespace Beutl.Media;

public enum BitmapColorType
{
    Unknown,
    Alpha8,
    Rgb565,
    Argb4444,
    Rgba8888,
    Rgb888x,
    Bgra8888,
    Rgba1010102,
    Bgra1010102,
    Rgb101010x,
    Bgr101010x,
    Bgr101010xXR,
    Gray8,
    RgbaF16,
    RgbaF16Clamped,
    RgbaF32,
    Rg88,
    AlphaF16,
    RgF16,
    Alpha16,
    Rg1616,
    Rgba16161616,
    Srgba8888,
    R8Unorm,
}

internal static class BitmapColorTypeExtensions
{
    public static SKColorType ToSKColorType(this BitmapColorType colorType) => colorType switch
    {
        BitmapColorType.Alpha8 => SKColorType.Alpha8,
        BitmapColorType.Rgb565 => SKColorType.Rgb565,
        BitmapColorType.Argb4444 => SKColorType.Argb4444,
        BitmapColorType.Rgba8888 => SKColorType.Rgba8888,
        BitmapColorType.Rgb888x => SKColorType.Rgb888x,
        BitmapColorType.Bgra8888 => SKColorType.Bgra8888,
        BitmapColorType.Rgba1010102 => SKColorType.Rgba1010102,
        BitmapColorType.Bgra1010102 => SKColorType.Bgra1010102,
        BitmapColorType.Rgb101010x => SKColorType.Rgb101010x,
        BitmapColorType.Bgr101010x => SKColorType.Bgr101010x,
        BitmapColorType.Bgr101010xXR => SKColorType.Bgr101010xXR,
        BitmapColorType.Gray8 => SKColorType.Gray8,
        BitmapColorType.RgbaF16 => SKColorType.RgbaF16,
        BitmapColorType.RgbaF16Clamped => SKColorType.RgbaF16Clamped,
        BitmapColorType.RgbaF32 => SKColorType.RgbaF32,
        BitmapColorType.Rg88 => SKColorType.Rg88,
        BitmapColorType.AlphaF16 => SKColorType.AlphaF16,
        BitmapColorType.RgF16 => SKColorType.RgF16,
        BitmapColorType.Alpha16 => SKColorType.Alpha16,
        BitmapColorType.Rg1616 => SKColorType.Rg1616,
        BitmapColorType.Rgba16161616 => SKColorType.Rgba16161616,
        BitmapColorType.Srgba8888 => SKColorType.Srgba8888,
        BitmapColorType.R8Unorm => SKColorType.R8Unorm,
        _ => SKColorType.Unknown,
    };

    public static BitmapColorType FromSKColorType(SKColorType colorType) => colorType switch
    {
        SKColorType.Alpha8 => BitmapColorType.Alpha8,
        SKColorType.Rgb565 => BitmapColorType.Rgb565,
        SKColorType.Argb4444 => BitmapColorType.Argb4444,
        SKColorType.Rgba8888 => BitmapColorType.Rgba8888,
        SKColorType.Rgb888x => BitmapColorType.Rgb888x,
        SKColorType.Bgra8888 => BitmapColorType.Bgra8888,
        SKColorType.Rgba1010102 => BitmapColorType.Rgba1010102,
        SKColorType.Bgra1010102 => BitmapColorType.Bgra1010102,
        SKColorType.Rgb101010x => BitmapColorType.Rgb101010x,
        SKColorType.Bgr101010x => BitmapColorType.Bgr101010x,
        SKColorType.Bgr101010xXR => BitmapColorType.Bgr101010xXR,
        SKColorType.Gray8 => BitmapColorType.Gray8,
        SKColorType.RgbaF16 => BitmapColorType.RgbaF16,
        SKColorType.RgbaF16Clamped => BitmapColorType.RgbaF16Clamped,
        SKColorType.RgbaF32 => BitmapColorType.RgbaF32,
        SKColorType.Rg88 => BitmapColorType.Rg88,
        SKColorType.AlphaF16 => BitmapColorType.AlphaF16,
        SKColorType.RgF16 => BitmapColorType.RgF16,
        SKColorType.Alpha16 => BitmapColorType.Alpha16,
        SKColorType.Rg1616 => BitmapColorType.Rg1616,
        SKColorType.Rgba16161616 => BitmapColorType.Rgba16161616,
        SKColorType.Srgba8888 => BitmapColorType.Srgba8888,
        SKColorType.R8Unorm => BitmapColorType.R8Unorm,
        _ => BitmapColorType.Unknown,
    };
}
