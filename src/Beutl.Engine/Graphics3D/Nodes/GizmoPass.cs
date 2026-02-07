using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Gizmo;
using Beutl.Media;

namespace Beutl.Graphics3D.Nodes;

/// <summary>
/// Gizmo pass for rendering 3D manipulation gizmos.
/// Renders directly to the lighting pass output texture with depth testing.
/// </summary>
public sealed class GizmoPass : GraphicsNode3D
{
    private readonly ITexture2D _depthTexture;
    private ITexture2D? _colorTexture;

    private IPipeline3D? _pipeline;
    private IDescriptorSet? _descriptorSet;
    private IBuffer? _uniformBuffer;

    // Gizmo geometry buffers
    private IBuffer? _translateVertexBuffer;
    private IBuffer? _translateIndexBuffer;
    private int _translateIndexCount;

    private IBuffer? _rotateVertexBuffer;
    private IBuffer? _rotateIndexBuffer;
    private int _rotateIndexCount;

    private IBuffer? _scaleVertexBuffer;
    private IBuffer? _scaleIndexBuffer;
    private int _scaleIndexCount;

    private bool _geometryInitialized;

    public GizmoPass(IGraphicsContext context, IShaderCompiler shaderCompiler, ITexture2D depthTexture)
        : base(context, shaderCompiler)
    {
        _depthTexture = depthTexture ?? throw new ArgumentNullException(nameof(depthTexture));
    }

    /// <summary>
    /// Gets the output texture (same as input color texture).
    /// </summary>
    public ITexture2D? OutputTexture => _colorTexture;

    /// <summary>
    /// Sets the color texture to render gizmos onto.
    /// </summary>
    public void SetColorTexture(ITexture2D colorTexture)
    {
        if (_colorTexture == colorTexture)
            return;

        _colorTexture = colorTexture;

        // Recreate framebuffer with new color texture
        if (RenderPass != null && _colorTexture != null)
        {
            Framebuffer?.Dispose();
            Framebuffer = Context.CreateFramebuffer3D(
                RenderPass,
                [_colorTexture],
                _depthTexture);
        }
    }

    protected override void OnInitialize(int width, int height)
    {
        CreateGizmoResources();
        InitializeGeometry();
    }

    protected override void OnResize(int width, int height)
    {
        // Framebuffer will be recreated when SetColorTexture is called
        // Nothing to do here as we don't own the color texture
    }

    private void CreateGizmoResources()
    {
        // Dispose old resources
        _pipeline?.Dispose();
        _descriptorSet?.Dispose();
        _uniformBuffer?.Dispose();
        Framebuffer?.Dispose();
        RenderPass?.Dispose();

        // Create render pass (single color attachment with depth)
        // Use Load for color to preserve existing content, Load for depth to use existing depth buffer
        RenderPass = Context.CreateRenderPass3D(
            [TextureFormat.RGBA8Unorm],
            TextureFormat.Depth32Float,
            AttachmentLoadOp.Load,  // Preserve color content
            AttachmentLoadOp.Load); // Use existing depth

        // Framebuffer will be created when SetColorTexture is called

        // Create uniform buffer
        _uniformBuffer = Context.CreateBuffer(
            (ulong)Marshal.SizeOf<GizmoUBO>(),
            BufferUsage.UniformBuffer,
            MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

        // Compile shaders
        var vertexSpirv = ShaderCompiler.CompileToSpirv(GizmoVertexShader, ShaderStage.Vertex);
        var fragmentSpirv = ShaderCompiler.CompileToSpirv(GizmoFragmentShader, ShaderStage.Fragment);

        // Descriptor bindings
        var descriptorBindings = new DescriptorBinding[]
        {
            new(0, DescriptorType.UniformBuffer, 1, ShaderStage.Vertex)
        };

        // Create pipeline with GizmoVertex input
        _pipeline = Context.CreatePipeline3D(
            RenderPass!,
            vertexSpirv,
            fragmentSpirv,
            descriptorBindings,
            GizmoVertex.GetVertexInputDescription());

        // Create descriptor set
        var poolSizes = new DescriptorPoolSize[]
        {
            new(DescriptorType.UniformBuffer, 1)
        };

        _descriptorSet = Context.CreateDescriptorSet(_pipeline, poolSizes);
        _descriptorSet.UpdateBuffer(0, _uniformBuffer);
    }

    private void InitializeGeometry()
    {
        if (_geometryInitialized)
            return;

        // Create translate gizmo
        GizmoMesh.CreateTranslateGizmo(out var translateVertices, out var translateIndices);
        _translateVertexBuffer = CreateVertexBuffer(translateVertices);
        _translateIndexBuffer = CreateIndexBuffer(translateIndices);
        _translateIndexCount = translateIndices.Length;

        // Create rotate gizmo
        GizmoMesh.CreateRotateGizmo(out var rotateVertices, out var rotateIndices);
        _rotateVertexBuffer = CreateVertexBuffer(rotateVertices);
        _rotateIndexBuffer = CreateIndexBuffer(rotateIndices);
        _rotateIndexCount = rotateIndices.Length;

        // Create scale gizmo
        GizmoMesh.CreateScaleGizmo(out var scaleVertices, out var scaleIndices);
        _scaleVertexBuffer = CreateVertexBuffer(scaleVertices);
        _scaleIndexBuffer = CreateIndexBuffer(scaleIndices);
        _scaleIndexCount = scaleIndices.Length;

        _geometryInitialized = true;
    }

    private IBuffer CreateVertexBuffer(GizmoVertex[] vertices)
    {
        var size = (ulong)(Marshal.SizeOf<GizmoVertex>() * vertices.Length);
        var buffer = Context.CreateBuffer(size, BufferUsage.VertexBuffer, MemoryProperty.HostVisible | MemoryProperty.HostCoherent);
        buffer.Upload(new ReadOnlySpan<GizmoVertex>(vertices));
        return buffer;
    }

    private IBuffer CreateIndexBuffer(uint[] indices)
    {
        var size = (ulong)(sizeof(uint) * indices.Length);
        var buffer = Context.CreateBuffer(size, BufferUsage.IndexBuffer, MemoryProperty.HostVisible | MemoryProperty.HostCoherent);
        buffer.Upload(new ReadOnlySpan<uint>(indices));
        return buffer;
    }

    /// <summary>
    /// Executes the gizmo pass.
    /// </summary>
    public void Execute(
        Camera.Camera3D.Resource camera,
        Object3D.Resource? gizmoTarget,
        GizmoMode gizmoMode,
        float aspectRatio)
    {
        if (Framebuffer == null || RenderPass == null || _pipeline == null || _descriptorSet == null)
            return;

        if (gizmoTarget == null || gizmoMode == GizmoMode.None)
            return;

        // Get gizmo geometry buffers
        IBuffer? vertexBuffer = null;
        IBuffer? indexBuffer = null;
        int indexCount = 0;

        switch (gizmoMode)
        {
            case GizmoMode.Translate:
                vertexBuffer = _translateVertexBuffer;
                indexBuffer = _translateIndexBuffer;
                indexCount = _translateIndexCount;
                break;
            case GizmoMode.Rotate:
                vertexBuffer = _rotateVertexBuffer;
                indexBuffer = _rotateIndexBuffer;
                indexCount = _rotateIndexCount;
                break;
            case GizmoMode.Scale:
                vertexBuffer = _scaleVertexBuffer;
                indexBuffer = _scaleIndexBuffer;
                indexCount = _scaleIndexCount;
                break;
        }

        if (vertexBuffer == null || indexBuffer == null || indexCount == 0)
            return;

        // Create model matrix
        // For Rotate and Scale modes, apply object's rotation so the gizmo aligns with the object
        Matrix4x4 modelMatrix;
        if (gizmoMode is GizmoMode.Rotate or GizmoMode.Scale)
        {
            // Apply rotation then translation
            var rotation = gizmoTarget.Rotation;
            var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(
                rotation.Y * MathF.PI / 180f,
                rotation.X * MathF.PI / 180f,
                rotation.Z * MathF.PI / 180f);
            modelMatrix = rotationMatrix * Matrix4x4.CreateTranslation(gizmoTarget.Position);
        }
        else
        {
            // Translate mode uses world-aligned gizmo
            modelMatrix = Matrix4x4.CreateTranslation(gizmoTarget.Position);
        }

        // Update uniform buffer
        var ubo = new GizmoUBO
        {
            Model = modelMatrix,
            View = camera.GetViewMatrix(),
            Projection = camera.GetProjectionMatrix(aspectRatio)
        };
        _uniformBuffer!.Upload(new ReadOnlySpan<GizmoUBO>(ref ubo));

        // Begin pass (loadOp is set to Load in CreateGizmoResources, so content is preserved)
        Span<Color> clearColors = [Colors.Transparent];
        BeginPass(clearColors, 1.0f);

        // Bind pipeline and descriptor set
        RenderPass.BindPipeline(_pipeline);
        RenderPass.BindDescriptorSet(_pipeline, _descriptorSet);

        // Bind vertex and index buffers
        RenderPass.BindVertexBuffer(vertexBuffer);
        RenderPass.BindIndexBuffer(indexBuffer);

        // Draw gizmo
        RenderPass.DrawIndexed((uint)indexCount);

        EndPass();
    }

    protected override void OnDispose()
    {
        _descriptorSet?.Dispose();
        _pipeline?.Dispose();
        _uniformBuffer?.Dispose();

        _translateVertexBuffer?.Dispose();
        _translateIndexBuffer?.Dispose();
        _rotateVertexBuffer?.Dispose();
        _rotateIndexBuffer?.Dispose();
        _scaleVertexBuffer?.Dispose();
        _scaleIndexBuffer?.Dispose();

        Framebuffer?.Dispose();
        RenderPass?.Dispose();
    }

    // === Gizmo Shaders ===

    private static string GizmoVertexShader => """
        #version 450

        layout(location = 0) in vec3 inPosition;
        layout(location = 1) in vec3 inColor;

        layout(binding = 0) uniform GizmoUBO {
            mat4 model;
            mat4 view;
            mat4 projection;
        } ubo;

        layout(location = 0) out vec3 fragColor;

        void main() {
            gl_Position = ubo.projection * ubo.view * ubo.model * vec4(inPosition, 1.0);
            fragColor = inColor;
        }
        """;

    private static string GizmoFragmentShader => """
        #version 450

        layout(location = 0) in vec3 fragColor;
        layout(location = 0) out vec4 outColor;

        void main() {
            outColor = vec4(fragColor, 1.0);
        }
        """;

    // === UBO Struct ===

    [StructLayout(LayoutKind.Sequential)]
    private struct GizmoUBO
    {
        public Matrix4x4 Model;
        public Matrix4x4 View;
        public Matrix4x4 Projection;
    }
}
