using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Meshes;

namespace Beutl.Graphics3D.Nodes;

internal static class MeshBufferUploadHelper
{
    public static void Ensure(IGraphicsContext context, Mesh.Resource meshResource)
    {
        if (!meshResource.BuffersDirty)
            return;

        var vertices = meshResource.GetVertices();
        var indices = meshResource.GetIndices();

        if (vertices.Length == 0 || indices.Length == 0)
            return;

        ulong vertexSize = (ulong)(vertices.Length * Marshal.SizeOf<Vertex3D>());
        ulong indexSize = (ulong)(indices.Length * sizeof(uint));

        IBuffer? oldVertexBuffer = meshResource.VertexBuffer;
        IBuffer? oldIndexBuffer = meshResource.IndexBuffer;
        meshResource.VertexBuffer = null;
        meshResource.IndexBuffer = null;
        Graphics3DDisposal.DisposeAll(oldVertexBuffer, oldIndexBuffer);

        IBuffer? vertexBuffer = null;
        IBuffer? indexBuffer = null;
        IBuffer? vertexStaging = null;
        IBuffer? indexStaging = null;
        try
        {
            vertexBuffer = context.CreateBuffer(
                vertexSize,
                BufferUsage.VertexBuffer | BufferUsage.TransferDestination,
                MemoryProperty.DeviceLocal);

            indexBuffer = context.CreateBuffer(
                indexSize,
                BufferUsage.IndexBuffer | BufferUsage.TransferDestination,
                MemoryProperty.DeviceLocal);

            vertexStaging = context.CreateBuffer(
                vertexSize,
                BufferUsage.TransferSource,
                MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

            indexStaging = context.CreateBuffer(
                indexSize,
                BufferUsage.TransferSource,
                MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

            vertexStaging.Upload(vertices);
            indexStaging.Upload(indices);

            context.CopyBuffer(vertexStaging, vertexBuffer, vertexSize);
            context.CopyBuffer(indexStaging, indexBuffer, indexSize);
        }
        catch
        {
            // Allocation/upload/copy is primary. Sweep every successfully-created local without allowing a
            // fallible native Dispose to replace that failure or strand a later buffer.
            Exception? ignoredCleanupFailure = null;
            Graphics3DDisposal.Capture(indexStaging, ref ignoredCleanupFailure);
            Graphics3DDisposal.Capture(vertexStaging, ref ignoredCleanupFailure);
            Graphics3DDisposal.Capture(indexBuffer, ref ignoredCleanupFailure);
            Graphics3DDisposal.Capture(vertexBuffer, ref ignoredCleanupFailure);
            throw;
        }

        // Publish only after staging teardown succeeds. A teardown failure also reclaims both unpublished
        // device-local buffers and reports the first cleanup exception.
        Exception? cleanupFailure = null;
        Graphics3DDisposal.Capture(indexStaging, ref cleanupFailure);
        Graphics3DDisposal.Capture(vertexStaging, ref cleanupFailure);
        if (cleanupFailure != null)
        {
            Graphics3DDisposal.Capture(indexBuffer, ref cleanupFailure);
            Graphics3DDisposal.Capture(vertexBuffer, ref cleanupFailure);
            Graphics3DDisposal.ThrowIfFailed(cleanupFailure);
        }

        meshResource.VertexBuffer = vertexBuffer;
        meshResource.IndexBuffer = indexBuffer;
        meshResource.BuffersDirty = false;
    }
}
