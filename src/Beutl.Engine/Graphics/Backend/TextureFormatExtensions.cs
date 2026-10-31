using Silk.NET.Vulkan;
using SkiaSharp;

namespace Beutl.Graphics.Backend;

/// <summary>
/// Extension methods for <see cref="TextureFormat"/>.
/// </summary>
public static class TextureFormatExtensions
{
    internal static NumericType GetNumericType(this TextureFormat format) => format switch
    {
        TextureFormat.R32SInt or TextureFormat.RGBA32SInt => NumericType.SInt,
        TextureFormat.R32UInt or TextureFormat.RGBA32UInt => NumericType.UInt,
        _ => NumericType.Float,
    };

    internal static int BytesPerTexel(this TextureFormat format) => format switch
    {
        TextureFormat.RGBA32Float or TextureFormat.RGBA32SInt or TextureFormat.RGBA32UInt => 16,
        TextureFormat.RGBA16Float or TextureFormat.RG32Float or TextureFormat.Depth32FloatStencil8 => 8,
        TextureFormat.R8Unorm => 1,
        TextureFormat.R16Float => 2,
        _ => 4,
    };

    /// <summary>
    /// Converts <see cref="TextureFormat"/> to Vulkan <see cref="Format"/>.
    /// </summary>
    public static Format ToVulkanFormat(this TextureFormat format)
    {
        return format switch
        {
            TextureFormat.RGBA8Unorm => Format.R8G8B8A8Unorm,
            TextureFormat.BGRA8Unorm => Format.B8G8R8A8Unorm,
            TextureFormat.RGBA16Float => Format.R16G16B16A16Sfloat,
            TextureFormat.RGBA32Float => Format.R32G32B32A32Sfloat,
            TextureFormat.R8Unorm => Format.R8Unorm,
            TextureFormat.R16Float => Format.R16Sfloat,
            TextureFormat.R32Float => Format.R32Sfloat,
            TextureFormat.Depth32Float => Format.D32Sfloat,
            TextureFormat.Depth24Stencil8 => Format.D24UnormS8Uint,
            TextureFormat.Depth32FloatStencil8 => Format.D32SfloatS8Uint,
            TextureFormat.RG32Float => Format.R32G32Sfloat,
            TextureFormat.R32SInt => Format.R32Sint,
            TextureFormat.R32UInt => Format.R32Uint,
            TextureFormat.RGBA32SInt => Format.R32G32B32A32Sint,
            TextureFormat.RGBA32UInt => Format.R32G32B32A32Uint,
            _ => Format.R8G8B8A8Unorm
        };
    }

    /// <summary>
    /// Converts <see cref="TextureFormat"/> to SkiaSharp <see cref="SKColorType"/>.
    /// </summary>
    public static SKColorType ToSkiaColorType(this TextureFormat format)
    {
        return format switch
        {
            TextureFormat.RGBA8Unorm => SKColorType.Rgba8888,
            TextureFormat.BGRA8Unorm => SKColorType.Bgra8888,
            TextureFormat.RGBA16Float => SKColorType.RgbaF16,
            TextureFormat.RGBA32Float => SKColorType.RgbaF32,
            TextureFormat.R8Unorm => SKColorType.Gray8,
            TextureFormat.R16Float => SKColorType.AlphaF16,
            TextureFormat.R32Float => SKColorType.RgbaF32, // Fallback: no single-channel F32
            TextureFormat.RG32Float or TextureFormat.R32SInt or TextureFormat.R32UInt
                or TextureFormat.RGBA32SInt or TextureFormat.RGBA32UInt => throw new NotSupportedException($"Skia does not support {format}."),
            _ => SKColorType.Rgba8888
        };
    }

    /// <summary>
    /// Gets the image aspect mask for this texture format.
    /// </summary>
    public static ImageAspectFlags GetAspectMask(this TextureFormat format)
    {
        return format switch
        {
            TextureFormat.Depth32Float => ImageAspectFlags.DepthBit,
            TextureFormat.Depth24Stencil8 or TextureFormat.Depth32FloatStencil8 => ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit,
            _ => ImageAspectFlags.ColorBit
        };
    }

    internal static bool HasStencil(this TextureFormat format)
        => format is TextureFormat.Depth24Stencil8 or TextureFormat.Depth32FloatStencil8;

    /// <summary>
    /// Returns true if this format is a depth or depth-stencil format.
    /// </summary>
    public static bool IsDepthFormat(this TextureFormat format)
    {
        return format is TextureFormat.Depth32Float or TextureFormat.Depth24Stencil8 or TextureFormat.Depth32FloatStencil8;
    }
}
