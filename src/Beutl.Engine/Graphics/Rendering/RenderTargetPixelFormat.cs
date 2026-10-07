using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>Identifies the pixel format required for a renderer-owned target allocation.</summary>
public enum RenderTargetPixelFormat : byte
{
    /// <summary>Linear-sRGB, premultiplied-alpha RGBA with 16-bit floating-point components.</summary>
    LinearPremultipliedRgba16Float,

    /// <summary>sRGB, premultiplied-alpha RGBA with 16-bit floating-point components.</summary>
    SrgbPremultipliedRgba16Float,
}

internal static class RenderTargetPixelFormatExtensions
{
    public static BitmapColorSpace GetColorSpace(this RenderTargetPixelFormat format) => format switch
    {
        RenderTargetPixelFormat.LinearPremultipliedRgba16Float => BitmapColorSpace.LinearSrgb,
        RenderTargetPixelFormat.SrgbPremultipliedRgba16Float => BitmapColorSpace.Srgb,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };
}
