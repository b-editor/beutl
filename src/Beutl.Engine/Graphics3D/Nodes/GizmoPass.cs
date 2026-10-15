using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Gizmo;
using Beutl.Graphics3D.Materials;
using Beutl.Media;

namespace Beutl.Graphics3D.Nodes;

/// <summary>
/// Gizmo pass for rendering 3D manipulation gizmos.
/// Renders directly to the lighting pass output texture, over the scene, depth testing only against itself.
/// </summary>
public sealed class GizmoPass : RenderNode3D
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

    private void CreateGizmoResources()
    {
        // Create render pass (single color attachment with depth)
        // Color is loaded to draw over the scene. Depth is cleared so the gizmo stays visible inside the
        // object it is attached to, while its own handles still occlude each other. This is the last pass to
        // use the shared depth buffer in a frame.
        RenderPass = Context.CreateRenderPass3D(
            [TextureFormat.RGBA16Float],
            TextureFormat.Depth32Float,
            AttachmentLoadOp.Load,
            AttachmentLoadOp.Clear);

        _uniformBuffer = MaterialGpuResources.CreateUniformBuffer<GizmoUBO>(Context);

        var vertexSpirv = ShaderCompiler.CompileToSpirv(GizmoVertexShader, ShaderStage.Vertex);
        var fragmentSpirv = ShaderCompiler.CompileToSpirv(GizmoFragmentShader, ShaderStage.Fragment);

        var descriptorBindings = new DescriptorBinding[]
        {
            new(0, DescriptorType.UniformBuffer, 1, ShaderStage.Vertex)
        };

        _pipeline = Context.CreatePipeline3D(
            RenderPass!,
            vertexSpirv,
            fragmentSpirv,
            descriptorBindings,
            GizmoVertex.GetVertexInputDescription());

        var poolSizes = new DescriptorPoolSize[]
        {
            new(DescriptorType.UniformBuffer, 1)
        };

        _descriptorSet = Context.CreateDescriptorSet(_pipeline, poolSizes);
        _descriptorSet.UpdateBuffer(0, _uniformBuffer);
    }

    private void InitializeGeometry()
    {
        GizmoMesh.CreateTranslateGizmo(out var translateVertices, out var translateIndices);
        _translateVertexBuffer = CreateVertexBuffer(translateVertices);
        _translateIndexBuffer = CreateIndexBuffer(translateIndices);
        _translateIndexCount = translateIndices.Length;

        GizmoMesh.CreateRotateGizmo(out var rotateVertices, out var rotateIndices);
        _rotateVertexBuffer = CreateVertexBuffer(rotateVertices);
        _rotateIndexBuffer = CreateIndexBuffer(rotateIndices);
        _rotateIndexCount = rotateIndices.Length;

        GizmoMesh.CreateScaleGizmo(out var scaleVertices, out var scaleIndices);
        _scaleVertexBuffer = CreateVertexBuffer(scaleVertices);
        _scaleIndexBuffer = CreateIndexBuffer(scaleIndices);
        _scaleIndexCount = scaleIndices.Length;
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
        Vector3 position,
        Quaternion orientation,
        GizmoMode gizmoMode,
        float aspectRatio)
    {
        if (Framebuffer == null || RenderPass == null || _pipeline == null || _descriptorSet == null)
            return;

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

        var scaleMatrix = Matrix4x4.CreateScale(GizmoHitTester.GetWorldScale(camera, position, aspectRatio));
        Matrix4x4 modelMatrix;
        if (gizmoMode is GizmoMode.Rotate or GizmoMode.Scale)
        {
            var rotationMatrix = Matrix4x4.CreateFromQuaternion(orientation);
            modelMatrix = scaleMatrix * rotationMatrix * Matrix4x4.CreateTranslation(position);
        }
        else
        {
            modelMatrix = scaleMatrix * Matrix4x4.CreateTranslation(position);
        }

        var ubo = new GizmoUBO
        {
            Model = modelMatrix,
            View = camera.GetViewMatrix(),
            Projection = camera.GetProjectionMatrix(aspectRatio)
        };
        _uniformBuffer!.Upload(new ReadOnlySpan<GizmoUBO>(ref ubo));

        // Begin pass (loadOp is set to Load in CreateGizmoResources, so content is preserved)
        Span<Color> clearColors = [Colors.Transparent];
        using (UsePass(clearColors, 1.0f))
        {
            RenderPass.BindPipeline(_pipeline);
            RenderPass.BindDescriptorSet(_pipeline, _descriptorSet);

            RenderPass.BindVertexBuffer(vertexBuffer);
            RenderPass.BindIndexBuffer(indexBuffer);

            RenderPass.DrawIndexed((uint)indexCount);
        }
    }

    protected override void OnDispose()
    {
        DisposeResources([_descriptorSet, _pipeline, _uniformBuffer, _translateVertexBuffer,
            _translateIndexBuffer, _rotateVertexBuffer, _rotateIndexBuffer, _scaleVertexBuffer,
            _scaleIndexBuffer, Framebuffer, RenderPass]);
    }

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

    [StructLayout(LayoutKind.Sequential)]
    private struct GizmoUBO
    {
        public Matrix4x4 Model;
        public Matrix4x4 View;
        public Matrix4x4 Projection;
    }
}
