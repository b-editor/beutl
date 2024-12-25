namespace Beutl.Graphics3D;

[Flags]
public enum TextureUsageFlags : uint
{
    Sampler = 0x1,
    ColorTarget = 0x2,
    DepthStencilTarget = 0x4,
    GraphicsStorageRead = 0x08,
    ComputeStorageRead = 0x10,
    ComputeStorageWrite = 0x20,
}
