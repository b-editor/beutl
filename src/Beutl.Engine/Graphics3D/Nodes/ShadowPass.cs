using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Meshes;
using Beutl.Media;

namespace Beutl.Graphics3D.Nodes;

/// <summary>
/// Shadow pass for rendering directional and spot light shadow maps.
/// </summary>
public sealed class ShadowPass : GraphicsNode3D
{
    /// <summary>
    /// Default shadow map resolution.
    /// </summary>
    public const int DefaultShadowMapSize = 2048;

    // Shader sources
    private const string ShadowVertexShader = @"
#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inTexCoord;

layout(push_constant) uniform PushConstants {
    mat4 model;
    mat4 lightViewProjection;
} pc;

void main() {
    gl_Position = pc.lightViewProjection * pc.model * vec4(inPosition, 1.0);
}
";

    private const string ShadowFragmentShader = @"
#version 450

// Minimal fragment shader - depth is written automatically
// We need a color output for the dummy attachment
layout(location = 0) out float outDummy;

void main() {
    outDummy = 1.0;
}
";

    [StructLayout(LayoutKind.Sequential)]
    private struct ShadowPushConstants
    {
        public Matrix4x4 Model;
        public Matrix4x4 LightViewProjection;
    }

    private IPipeline3D? _shadowPipeline;
    private byte[]? _vertexShaderSpirv;
    private byte[]? _fragmentShaderSpirv;

    // Minimal dummy color attachment format
    private static readonly TextureFormat[] ShadowPassFormats = [TextureFormat.R8Unorm];

    public ShadowPass(IGraphicsContext context, IShaderCompiler shaderCompiler)
        : base(context, shaderCompiler)
    {
    }

    /// <summary>
    /// Gets the shadow depth texture.
    /// </summary>
    public ITexture2D? ShadowDepthTexture { get; private set; }

    /// <summary>
    /// Gets the dummy color texture (required for framebuffer).
    /// </summary>
    public ITexture2D? DummyColorTexture { get; private set; }

    /// <summary>
    /// Gets the light view matrix.
    /// </summary>
    public Matrix4x4 LightViewMatrix { get; private set; }

    /// <summary>
    /// Gets the light projection matrix.
    /// </summary>
    public Matrix4x4 LightProjectionMatrix { get; private set; }

    /// <summary>
    /// Gets the combined light view-projection matrix.
    /// In C# System.Numerics: View * Projection (same as Camera3D.GetViewProjectionMatrix)
    /// </summary>
    public Matrix4x4 LightViewProjection => LightViewMatrix * LightProjectionMatrix;

    protected override void OnInitialize(int width, int height)
    {
        // Shadow pass uses its own fixed size
        CreateShadowMap(DefaultShadowMapSize, DefaultShadowMapSize);
        CompileShaders();
        CreatePipeline();
    }

    protected override void OnResize(int width, int height)
    {
        // Shadow map size is independent of screen size
        // Only recreate if needed for custom sizes
    }

    /// <summary>
    /// Resizes the shadow map to a custom size.
    /// </summary>
    public void ResizeShadowMap(int size)
    {
        CreateShadowMap(size, size);
    }

    private void CreateShadowMap(int width, int height)
    {
        // Dispose old resources
        Framebuffer?.Dispose();
        RenderPass?.Dispose();
        ShadowDepthTexture?.Dispose();
        DummyColorTexture?.Dispose();

        // Create shadow depth texture
        ShadowDepthTexture = Context.CreateTexture2D(width, height, TextureFormat.Depth32Float);

        // Create minimal dummy color texture (required for framebuffer)
        DummyColorTexture = Context.CreateTexture2D(width, height, TextureFormat.R8Unorm);

        // Create shadow render pass and framebuffer
        RenderPass = Context.CreateRenderPass3D(ShadowPassFormats, TextureFormat.Depth32Float);
        Framebuffer = Context.CreateFramebuffer3D(
            RenderPass,
            [DummyColorTexture],
            ShadowDepthTexture);
    }

    private void CompileShaders()
    {
        _vertexShaderSpirv = ShaderCompiler.CompileToSpirv(ShadowVertexShader, ShaderStage.Vertex);
        _fragmentShaderSpirv = ShaderCompiler.CompileToSpirv(ShadowFragmentShader, ShaderStage.Fragment);
    }

    private void CreatePipeline()
    {
        if (RenderPass == null || _vertexShaderSpirv == null || _fragmentShaderSpirv == null)
            return;

        _shadowPipeline?.Dispose();

        // No descriptor bindings needed - we only use push constants
        var descriptorBindings = Array.Empty<DescriptorBinding>();

        var options = new PipelineOptions
        {
            DepthTestEnabled = true,
            DepthWriteEnabled = true,
            CullMode = CullMode.Back
        };

        _shadowPipeline = Context.CreatePipeline3D(
            RenderPass,
            _vertexShaderSpirv,
            _fragmentShaderSpirv,
            descriptorBindings,
            Vertex3D.GetVertexInputDescription(),
            options);
    }

    /// <summary>
    /// Sets up the shadow pass for a directional light.
    /// </summary>
    public void SetupForDirectionalLight(DirectionalLight3D.Resource light, Vector3 sceneCenter, float sceneRadius)
    {
        var direction = light.Direction;
        if (direction == Vector3.Zero)
            direction = new Vector3(0, -1, 0);
        direction = Vector3.Normalize(direction);

        // Position the light "camera" behind the scene, looking at the center
        var shadowDistance = light.ShadowDistance;
        var lightPosition = sceneCenter - direction * shadowDistance * 0.5f;

        // Create view matrix looking at scene center
        var up = Math.Abs(Vector3.Dot(direction, Vector3.UnitY)) > 0.99f
            ? Vector3.UnitZ
            : Vector3.UnitY;
        LightViewMatrix = Matrix4x4.CreateLookAt(lightPosition, sceneCenter, up);

        // Orthographic projection for directional light
        var size = light.ShadowMapSize;
        LightProjectionMatrix = Matrix4x4.CreateOrthographic(size, size, 0.1f, shadowDistance);
    }

    /// <summary>
    /// Sets up the shadow pass for a spot light.
    /// </summary>
    public void SetupForSpotLight(SpotLight3D.Resource light)
    {
        var position = light.Position;
        var direction = light.Direction;
        if (direction == Vector3.Zero)
            direction = new Vector3(0, -1, 0);
        direction = Vector3.Normalize(direction);

        // Create view matrix from light position looking in light direction
        var up = Math.Abs(Vector3.Dot(direction, Vector3.UnitY)) > 0.99f
            ? Vector3.UnitZ
            : Vector3.UnitY;
        var target = position + direction;
        LightViewMatrix = Matrix4x4.CreateLookAt(position, target, up);

        // Perspective projection based on spot light cone angle
        var fovRadians = light.OuterConeAngle * 2f * MathF.PI / 180f;
        fovRadians = Math.Clamp(fovRadians, 0.1f, MathF.PI - 0.1f);
        LightProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(
            fovRadians,
            1.0f,  // Square shadow map
            0.1f,
            light.Range);
    }

    /// <summary>
    /// Executes the shadow pass for the configured light.
    /// </summary>
    public void Execute(IReadOnlyList<Object3D.Resource> objects)
    {
        if (Framebuffer == null || RenderPass == null || _shadowPipeline == null)
            return;

        // Begin shadow pass with clear (clear to max depth)
        Span<Color> clearColors = [new Color(255, 255, 255, 255)]; // Dummy color
        BeginPass(clearColors);

        // Bind shadow pipeline
        RenderPass.BindPipeline(_shadowPipeline);

        var lightVP = LightViewProjection;

        // Render each object
        foreach (var obj in objects)
        {
            RenderObject(obj, lightVP, Matrix4x4.Identity);
        }

        EndPass();
    }

    private void RenderObject(Object3D.Resource obj, Matrix4x4 lightVP, Matrix4x4 parentMatrix)
    {
        if (!obj.IsEnabled)
            return;

        // Calculate combined world matrix
        var worldMatrix = obj.GetWorldMatrix() * parentMatrix;

        // Render children if any
        var children = obj.GetChildResources();
        foreach (var child in children)
        {
            RenderObject(child, lightVP, worldMatrix);
        }

        // Render this object's mesh if any
        RenderMesh(obj, lightVP, worldMatrix);
    }

    private void RenderMesh(Object3D.Resource obj, Matrix4x4 lightVP, Matrix4x4 worldMatrix)
    {
        var meshResource = obj.GetMesh();
        if (meshResource == null)
            return;

        EnsureMeshBuffers(meshResource);

        if (meshResource.VertexBuffer == null || meshResource.IndexBuffer == null)
            return;

        // Set push constants with model and light VP matrices
        var pushConstants = new ShadowPushConstants
        {
            Model = worldMatrix,
            LightViewProjection = lightVP
        };
        RenderPass!.SetPushConstants(pushConstants);

        // Bind vertex and index buffers
        RenderPass.BindVertexBuffer(meshResource.VertexBuffer);
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
        _shadowPipeline?.Dispose();
        Framebuffer?.Dispose();
        RenderPass?.Dispose();
        ShadowDepthTexture?.Dispose();
        DummyColorTexture?.Dispose();
    }
}
