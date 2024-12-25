namespace Beutl.Graphics3D;

[Flags]
public enum ShaderFormat : uint
{
    Private = 0x1,
    SPIRV = 0x2,
    DXBC = 0x4,
    DXIL = 0x08,
    MSL = 0x10,
    MetalLib = 0x20,
}
