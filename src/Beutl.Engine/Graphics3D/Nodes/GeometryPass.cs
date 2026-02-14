using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Meshes;
using Beutl.Media;

namespace Beutl.Graphics3D.Nodes;

/// <summary>
/// Geometry pass for deferred rendering.
/// Renders objects to the G-Buffer (Position, Normal+Metallic, Albedo+Roughness, Emission+AO, Depth).
/// </summary>
public sealed class GeometryPass : GraphicsNode3D
{
    // G-Buffer formats
    public static readonly TextureFormat[] GBufferFormats =
    [
        TextureFormat.RGBA16Float,  // Position
        TextureFormat.RGBA16Float,  // Normal + Metallic
        TextureFormat.RGBA8Unorm,   // Albedo + Roughness
        TextureFormat.RGBA16Float   // Emission + AO
    ];

    // Default material for objects without a material
    private readonly BasicMaterial _defaultMaterial = new();
    private BasicMaterial.Resource? _defaultMaterialResource;

    public GeometryPass(IGraphicsContext context, IShaderCompiler shaderCompiler)
        : base(context, shaderCompiler)
    {
    }

    /// <summary>
    /// Gets the world position texture (RGB16F).
    /// </summary>
    public ITexture2D? PositionTexture { get; private set; }

    /// <summary>
    /// Gets the normal + metallic texture (RGBA16F: Normal in RGB, Metallic in A).
    /// </summary>
    public ITexture2D? NormalMetallicTexture { get; private set; }

    /// <summary>
    /// Gets the albedo + roughness texture (RGBA8: Albedo in RGB, Roughness in A).
    /// </summary>
    public ITexture2D? AlbedoRoughnessTexture { get; private set; }

    /// <summary>
    /// Gets the emission + AO texture (RGBA16F: Emission in RGB, AO in A).
    /// </summary>
    public ITexture2D? EmissionAOTexture { get; private set; }

    /// <summary>
    /// Gets the depth texture (D32F).
    /// </summary>
    public ITexture2D? DepthTexture { get; private set; }

    protected override void OnInitialize(int width, int height)
    {
        CreateGBuffer(width, height);
        _defaultMaterialResource = _defaultMaterial.ToResource(new RenderContext(TimeSpan.Zero));
    }

    protected override void OnResize(int width, int height)
    {
        CreateGBuffer(width, height);
    }

    private void CreateGBuffer(int width, int height)
    {
        // Dispose old resources
        Framebuffer?.Dispose();
        RenderPass?.Dispose();
        PositionTexture?.Dispose();
        NormalMetallicTexture?.Dispose();
        AlbedoRoughnessTexture?.Dispose();
        EmissionAOTexture?.Dispose();
        DepthTexture?.Dispose();

        // Create G-Buffer textures
        PositionTexture = Context.CreateTexture2D(width, height, TextureFormat.RGBA16Float);
        NormalMetallicTexture = Context.CreateTexture2D(width, height, TextureFormat.RGBA16Float);
        AlbedoRoughnessTexture = Context.CreateTexture2D(width, height, TextureFormat.RGBA8Unorm);
        EmissionAOTexture = Context.CreateTexture2D(width, height, TextureFormat.RGBA16Float);
        DepthTexture = Context.CreateTexture2D(width, height, TextureFormat.Depth32Float);

        // Create geometry render pass and framebuffer
        RenderPass = Context.CreateRenderPass3D(GBufferFormats, TextureFormat.Depth32Float);
        Framebuffer = Context.CreateFramebuffer3D(
            RenderPass,
            new[] { PositionTexture, NormalMetallicTexture, AlbedoRoughnessTexture, EmissionAOTexture },
            DepthTexture);
    }

    /// <summary>
    /// Executes the geometry pass, rendering all objects to the G-Buffer.
    /// </summary>
    public void Execute(
        RenderContext renderContext,
        Camera.Camera3D.Resource camera,
        IReadOnlyList<Object3D.Resource> objects,
        float aspectRatio,
        IReadOnlyList<LightData> lightDataList,
        Color ambientColor,
        float ambientIntensity)
    {
        if (Framebuffer == null || RenderPass == null)
            return;

        // Create render context for materials
        var renderContext3D = new RenderContext3D(
            Context,
            RenderPass,
            ShaderCompiler,
            camera.GetViewMatrix(),
            camera.GetProjectionMatrix(aspectRatio),
            camera.Position,
            new Vector3(ambientColor.R / 255f, ambientColor.G / 255f, ambientColor.B / 255f) * ambientIntensity,
            lightDataList,
            renderContext);

        // Clear colors for G-Buffer (black/zero for most, except normal which should be (0,0,1) for up)
        Span<Color> clearColors =
        [
            new Color(0, 0, 0, 0),       // Position (black, alpha=0 for invalid background)
            new Color(255, 128, 128, 0), // Normal (0.5,0.5,1 = up) + Metallic=0
            new Color(0, 0, 0, 0),       // Albedo (black) + Roughness=0
            new Color(255, 0, 0, 0)      // Emission (none) + AO=1
        ];

        // Begin geometry pass
        BeginPass(clearColors);

        // Render each object
        foreach (var obj in objects)
        {
            RenderObject(renderContext3D, obj, Matrix4x4.Identity);
        }

        EndPass();
    }

    private void RenderObject(RenderContext3D renderContext3D, Object3D.Resource obj, Matrix4x4 parentMatrix)
    {
        if (!obj.IsEnabled)
            return;

        // Calculate combined world matrix
        var worldMatrix = obj.GetWorldMatrix() * parentMatrix;

        // Render children if any
        var children = obj.GetChildResources();
        foreach (var child in children)
        {
            RenderObject(renderContext3D, child, worldMatrix);
        }

        // Render this object's mesh if any
        RenderMesh(renderContext3D, obj, worldMatrix);
    }

    private void RenderMesh(RenderContext3D renderContext3D, Object3D.Resource obj, Matrix4x4 worldMatrix)
    {
        // Get mesh resource from object
        var meshResource = obj.GetMesh();
        if (meshResource == null)
            return;

        // Ensure GPU buffers are created/updated
        EnsureMeshBuffers(meshResource);

        if (meshResource.VertexBuffer == null || meshResource.IndexBuffer == null)
            return;

        // Get material resource (use default if not set)
        var materialResource = obj.Material ?? _defaultMaterialResource;
        if (materialResource == null)
            return;

        // Ensure material pipeline is created
        materialResource.EnsurePipeline(renderContext3D);

        // Bind material (pipeline, uniforms, descriptor sets) with combined matrix
        materialResource.Bind(renderContext3D, obj, worldMatrix);

        // Bind vertex and index buffers
        RenderPass!.BindVertexBuffer(meshResource.VertexBuffer);
        RenderPass.BindIndexBuffer(meshResource.IndexBuffer);

        // Draw the mesh
        RenderPass.DrawIndexed((uint)meshResource.IndexCount);
    }

    private void EnsureMeshBuffers(Mesh.Resource meshResource)
    {
        if (!meshResource.BuffersDirty)
            return;

        var vertices = meshResource.GetVertices();
        var indices = meshResource.GetIndices();

        if (vertices.Length == 0 || indices.Length == 0)
            return;

        ulong vertexSize = (ulong)(vertices.Length * Marshal.SizeOf<Vertex3D>());
        ulong indexSize = (ulong)(indices.Length * sizeof(uint));

        // Dispose old buffers if they exist
        meshResource.VertexBuffer?.Dispose();
        meshResource.IndexBuffer?.Dispose();

        // Create new device-local buffers
        var vertexBuffer = Context.CreateBuffer(
            vertexSize,
            BufferUsage.VertexBuffer | BufferUsage.TransferDestination,
            MemoryProperty.DeviceLocal);

        var indexBuffer = Context.CreateBuffer(
            indexSize,
            BufferUsage.IndexBuffer | BufferUsage.TransferDestination,
            MemoryProperty.DeviceLocal);

        // Create staging buffers and upload
        using var vertexStaging = Context.CreateBuffer(
            vertexSize,
            BufferUsage.TransferSource,
            MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

        using var indexStaging = Context.CreateBuffer(
            indexSize,
            BufferUsage.TransferSource,
            MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

        vertexStaging.Upload(vertices);
        indexStaging.Upload(indices);

        // Copy from staging to device local
        Context.CopyBuffer(vertexStaging, vertexBuffer, vertexSize);
        Context.CopyBuffer(indexStaging, indexBuffer, indexSize);

        // Store in mesh resource
        meshResource.VertexBuffer = vertexBuffer;
        meshResource.IndexBuffer = indexBuffer;
        meshResource.BuffersDirty = false;
    }

    protected override void OnDispose()
    {
        _defaultMaterialResource?.Dispose();

        Framebuffer?.Dispose();
        RenderPass?.Dispose();
        PositionTexture?.Dispose();
        NormalMetallicTexture?.Dispose();
        AlbedoRoughnessTexture?.Dispose();
        EmissionAOTexture?.Dispose();
        DepthTexture?.Dispose();
    }
}
