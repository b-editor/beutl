namespace Beutl.Graphics3D;

[Flags]
public enum BufferUsageFlags : uint
{
    Vertex = 0x1,
    Index = 0x2,
    Indirect = 0x4,
    GraphicsStorageRead = 0x08,
    ComputeStorageRead = 0x10,
    ComputeStorageWrite = 0x20,
}
