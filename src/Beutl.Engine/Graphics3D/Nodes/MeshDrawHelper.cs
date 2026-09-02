using System.Numerics;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Meshes;

namespace Beutl.Graphics3D.Nodes;

internal static class MeshDrawHelper
{
    public static Mesh.Resource? Prepare(IGraphicsContext context, Object3D.Resource obj)
    {
        var meshResource = obj.GetMesh();
        if (meshResource == null)
            return null;

        MeshBufferUploadHelper.Ensure(context, meshResource);

        if (meshResource.VertexBuffer == null || meshResource.IndexBuffer == null)
            return null;

        return meshResource;
    }

    public static void Draw(IRenderPass3D renderPass, Mesh.Resource meshResource)
    {
        renderPass.BindVertexBuffer(meshResource.VertexBuffer!);
        renderPass.BindIndexBuffer(meshResource.IndexBuffer!);
        renderPass.DrawIndexed((uint)meshResource.UploadedIndexCount);
    }

    public static void DrawWithMaterial(
        in RenderContext3D context,
        Object3D.Resource obj,
        Matrix4x4 worldMatrix,
        Material3D.Resource? defaultMaterial)
    {
        var meshResource = Prepare(context.GraphicsContext, obj);
        if (meshResource == null)
            return;

        var materialResource = obj.Material ?? defaultMaterial;
        if (materialResource == null)
            return;

        materialResource.EnsurePipeline(context);
        materialResource.Bind(context, obj, worldMatrix);

        Draw(context.RenderPass, meshResource);
    }
}
