using System.Runtime.InteropServices;

namespace Beutl.Graphics3D.Meshes;

public class MeshResource : IDisposable
{
    public MeshResource(Device device, Mesh mesh)
    {
        Mesh = mesh;
        VertexBuffer = Buffer.Create<Vertex>(device, BufferUsageFlags.Vertex, (uint)mesh.Vertices.Count);
        IndexBuffer = Buffer.Create<uint>(device, BufferUsageFlags.Index, (uint)mesh.Indices!.Length);
        VertexTransferBuffer =
            TransferBuffer.Create<Vertex>(device, TransferBufferUsage.Upload, (uint)mesh.Vertices.Count);
        IndexTransferBuffer =
            TransferBuffer.Create<uint>(device, TransferBufferUsage.Upload, (uint)mesh.Indices.Length);
    }

    public Buffer VertexBuffer { get; }

    public Buffer IndexBuffer { get; }

    public TransferBuffer VertexTransferBuffer { get; }

    public TransferBuffer IndexTransferBuffer { get; }

    public Mesh Mesh { get; }

    public void OnCopyPass(CopyPass pass)
    {
        using (MappedBuffer vertexBuffer = VertexTransferBuffer.Map())
        {
            CollectionsMarshal.AsSpan(Mesh.Vertices).CopyTo(vertexBuffer.AsSpan<Vertex>());
        }

        pass.UploadToBuffer(VertexTransferBuffer, VertexBuffer, false);
        
        using (MappedBuffer indexBuffer = IndexTransferBuffer.Map())
        {
            Mesh.Indices!.AsSpan().CopyTo(indexBuffer.AsSpan<int>());
        }
        
        pass.UploadToBuffer(IndexTransferBuffer, IndexBuffer, false);
    }

    public void Dispose()
    {
        VertexBuffer.Dispose();
        IndexBuffer.Dispose();
        VertexTransferBuffer.Dispose();
        IndexTransferBuffer.Dispose();
    }
}
