namespace Beutl.Graphics.Backend;

[Flags]
internal enum BufferUsage
{
    None = 0,
    VertexBuffer = 1 << 0,
    IndexBuffer = 1 << 1,
    UniformBuffer = 1 << 2,
    StorageBuffer = 1 << 3,
    TransferSrc = 1 << 4,
    TransferDst = 1 << 5
}
