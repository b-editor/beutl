using System.Numerics;
using Beutl.Graphics.Backend;

namespace Beutl.Graphics.Shaders;

public enum ImageKind { Image2D, Image2DArray, Image3D, Cube, CubeArray }

/// <summary>A fixed texel extent, independent of preview scale. Cube layer counts include all six faces.</summary>
public readonly record struct ImageDescription(ImageKind Kind, int Width, int Height,
    TextureFormat Format = TextureFormat.RGBA16Float, int Depth = 1, int ArrayLayers = 1, int MipLevels = 1)
{
    internal void Validate()
    {
        if (!Enum.IsDefined(Kind) || !Enum.IsDefined(Format))
            throw new ArgumentException("Unknown image kind or format.");
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Height);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Depth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ArrayLayers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MipLevels);
        if (MipLevels > 1 + BitOperations.Log2((uint)Math.Max(Math.Max(Width, Height), Depth)))
            throw new ArgumentException("Mip count exceeds the image extent.");
        if (Kind != ImageKind.Image3D && Depth != 1 || Kind is ImageKind.Image2D or ImageKind.Image3D && ArrayLayers != 1
            || Kind == ImageKind.Cube && ArrayLayers != 6
            || Kind is ImageKind.Cube or ImageKind.CubeArray && (Width != Height || ArrayLayers % 6 != 0))
            throw new ArgumentException("The extent or layer count does not match the image kind.");
        if (Kind == ImageKind.Image3D && Format.IsDepthFormat())
            throw new ArgumentException("Vulkan depth/stencil images must be two-dimensional.");
    }

    internal (int Width, int Height, int Depth) MipExtent(int mip)
        => (Math.Max(1, Width >> mip), Math.Max(1, Height >> mip), Math.Max(1, Depth >> mip));
}

/// <summary>Four clear components with the numeric type required by the image format.</summary>
public readonly record struct ImageClearValue
{
    private ImageClearValue(uint x, uint y, uint z, uint w, NumericType category)
        => (X, Y, Z, W, Category) = (x, y, z, w, category);

    internal uint X { get; }
    internal uint Y { get; }
    internal uint Z { get; }
    internal uint W { get; }
    internal NumericType Category { get; }

    public static ImageClearValue Float(Vector4 value) => new(BitConverter.SingleToUInt32Bits(value.X),
        BitConverter.SingleToUInt32Bits(value.Y), BitConverter.SingleToUInt32Bits(value.Z), BitConverter.SingleToUInt32Bits(value.W), NumericType.Float);
    public static ImageClearValue Int(int x, int y = 0, int z = 0, int w = 0)
        => new(unchecked((uint)x), unchecked((uint)y), unchecked((uint)z), unchecked((uint)w), NumericType.SInt);
    public static ImageClearValue UInt(uint x, uint y = 0, uint z = 0, uint w = 0) => new(x, y, z, w, NumericType.UInt);
}
