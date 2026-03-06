using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Meshes;
using Beutl.Media;

namespace Beutl.Graphics3D.Nodes;

/// <summary>
/// Shadow pass for rendering point light shadow cube maps.
/// Renders 6 faces for omnidirectional shadows.
/// </summary>
public sealed class PointShadowPass : GraphicsNode3D
{
    /// <summary>
    /// Default cube face size for point light shadow maps.
    /// </summary>
    public const int DefaultCubeFaceSize = 1024;

    // Shader sources - outputs linear depth for omnidirectional shadows
    // Push constants: Model (64 bytes) + LightViewProjection (64 bytes) = 128 bytes
    // UBO: LightPos + FarPlane
    private const string PointShadowVertexShader = @"
#version 450

layout(location = 0) in vec3 inPosition;
layout(location = 1) in vec3 inNormal;
layout(location = 2) in vec2 inTexCoord;

layout(location = 0) out vec3 fragWorldPos;

layout(push_constant) uniform PushConstants {
    mat4 model;
    mat4 lightViewProjection;
} pc;

void main() {
    vec4 worldPos = pc.model * vec4(inPosition, 1.0);
    fragWorldPos = worldPos.xyz;
    gl_Position = pc.lightViewProjection * worldPos;
}
";

    private const string PointShadowFragmentShader = @"
#version 450

layout(location = 0) in vec3 fragWorldPos;

layout(location = 0) out float outDummy;

layout(binding = 0) uniform LightData {
    vec3 lightPos;
    float farPlane;
} light;

void main() {
    // Output linear distance from light
    float distance = length(fragWorldPos - light.lightPos);
    gl_FragDepth = distance / light.farPlane;
    outDummy = 1.0;
}
";

    // Push constants: 128 bytes exactly (2 matrices)
    [StructLayout(LayoutKind.Sequential)]
    private struct PointShadowPushConstants
    {
        public Matrix4x4 Model;
        public Matrix4x4 LightViewProjection;
    }

    // UBO for light data: 16 bytes (vec3 + float with padding)
    [StructLayout(LayoutKind.Sequential)]
    private struct LightDataUBO
    {
        public Vector3 LightPos;
        public float FarPlane;
    }

    // Cube face directions and up vectors
    private static readonly (Vector3 Direction, Vector3 Up)[] s_cubeFaceDirections =
    [
        (Vector3.UnitX, -Vector3.UnitY),   // +X (Right)
        (-Vector3.UnitX, -Vector3.UnitY),  // -X (Left)
        (Vector3.UnitY, Vector3.UnitZ),    // +Y (Up)
        (-Vector3.UnitY, -Vector3.UnitZ),  // -Y (Down)
        (Vector3.UnitZ, -Vector3.UnitY),   // +Z (Front)
        (-Vector3.UnitZ, -Vector3.UnitY)   // -Z (Back)
    ];

    private IPipeline3D? _shadowPipeline;
    private IDescriptorSet? _descriptorSet;
    private IBuffer? _lightDataBuffer;
    private byte[]? _vertexShaderSpirv;
    private byte[]? _fragmentShaderSpirv;

    // Per-face resources
    private readonly ITexture2D?[] _faceDummyTextures = new ITexture2D?[6];
    private readonly ITexture2D?[] _faceDepthTextures = new ITexture2D?[6];
    private readonly IFramebuffer3D?[] _faceFramebuffers = new IFramebuffer3D?[6];
    private readonly Matrix4x4[] _lightViewMatrices = new Matrix4x4[6];
    private Matrix4x4 _lightProjectionMatrix;
    private Vector3 _lightPosition;
    private float _farPlane;

    // Minimal dummy color attachment format
    private static readonly TextureFormat[] s_shadowPassFormats = [TextureFormat.R8Unorm];

    public PointShadowPass(IGraphicsContext context, IShaderCompiler shaderCompiler)
        : base(context, shaderCompiler)
    {
    }

    /// <summary>
    /// Gets the shadow cube map texture.
    /// </summary>
    public ITextureCube? ShadowCubeTexture { get; private set; }

    /// <summary>
    /// Gets the depth textures for each cube face (for copying to cube array).
    /// </summary>
    internal IReadOnlyList<ITexture2D?> FaceDepthTextures => _faceDepthTextures;

    /// <summary>
    /// Gets the light position for this shadow.
    /// </summary>
    public Vector3 LightPosition => _lightPosition;

    /// <summary>
    /// Gets the far plane distance for depth linearization.
    /// </summary>
    public float FarPlane => _farPlane;

    /// <summary>
    /// Gets the light view matrices for each cube face.
    /// </summary>
    public IReadOnlyList<Matrix4x4> LightViewMatrices => _lightViewMatrices;

    /// <summary>
    /// Gets the light projection matrix (same for all faces).
    /// </summary>
    public Matrix4x4 LightProjectionMatrix => _lightProjectionMatrix;

    protected override void OnInitialize(int width, int height)
    {
        // Point shadow pass uses its own fixed size
        CreateShadowCubeMap(DefaultCubeFaceSize);
        CompileShaders();
        CreatePipeline();
        CreateLightDataBuffer();
    }

    protected override void OnResize(int width, int height)
    {
        // Shadow map size is independent of screen size
    }

    /// <summary>
    /// Resizes the shadow cube map to a custom face size.
    /// </summary>
    public void ResizeShadowMap(int faceSize)
    {
        CreateShadowCubeMap(faceSize);
    }

    private void CreateShadowCubeMap(int faceSize)
    {
        // Dispose old resources
        DisposeFaceResources();
        RenderPass?.Dispose();
        ShadowCubeTexture?.Dispose();

        // Create shadow cube map texture
        ShadowCubeTexture = Context.CreateTextureCube(faceSize, TextureFormat.Depth32Float);

        // Create render pass (shared for all faces)
        RenderPass = Context.CreateRenderPass3D(s_shadowPassFormats, TextureFormat.Depth32Float);

        // Create per-face resources
        for (int i = 0; i < 6; i++)
        {
            // Create dummy color texture for this face
            _faceDummyTextures[i] = Context.CreateTexture2D(faceSize, faceSize, TextureFormat.R8Unorm);

            // Create depth texture for this face
            _faceDepthTextures[i] = Context.CreateTexture2D(faceSize, faceSize, TextureFormat.Depth32Float);

            // Create framebuffer with the face's depth texture
            _faceFramebuffers[i] = Context.CreateFramebuffer3D(
                RenderPass,
                [_faceDummyTextures[i]],
                _faceDepthTextures[i]);
        }
    }

    private void DisposeFaceResources()
    {
        for (int i = 0; i < 6; i++)
        {
            _faceFramebuffers[i]?.Dispose();
            _faceFramebuffers[i] = null;
            _faceDummyTextures[i]?.Dispose();
            _faceDummyTextures[i] = null;
            _faceDepthTextures[i]?.Dispose();
            _faceDepthTextures[i] = null;
        }
    }

    private void CompileShaders()
    {
        _vertexShaderSpirv = ShaderCompiler.CompileToSpirv(PointShadowVertexShader, ShaderStage.Vertex);
        _fragmentShaderSpirv = ShaderCompiler.CompileToSpirv(PointShadowFragmentShader, ShaderStage.Fragment);
    }

    private void CreatePipeline()
    {
        if (RenderPass == null || _vertexShaderSpirv == null || _fragmentShaderSpirv == null)
            return;

        _shadowPipeline?.Dispose();
        _descriptorSet?.Dispose();

        // Descriptor binding for light data UBO
        var descriptorBindings = new DescriptorBinding[]
        {
            new(0, DescriptorType.UniformBuffer, 1, ShaderStage.Fragment)
        };

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

        // Create descriptor set for light data
        var poolSizes = new DescriptorPoolSize[]
        {
            new(DescriptorType.UniformBuffer, 1)
        };
        _descriptorSet = Context.CreateDescriptorSet(_shadowPipeline, poolSizes);
    }

    private void CreateLightDataBuffer()
    {
        _lightDataBuffer?.Dispose();
        _lightDataBuffer = Context.CreateBuffer(
            (ulong)Marshal.SizeOf<LightDataUBO>(),
            BufferUsage.UniformBuffer,
            MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

        // Bind to descriptor set
        _descriptorSet?.UpdateBuffer(0, _lightDataBuffer);
    }

    private void UpdateLightData()
    {
        if (_lightDataBuffer == null)
            return;

        var lightData = new LightDataUBO
        {
            LightPos = _lightPosition,
            FarPlane = _farPlane
        };
        _lightDataBuffer.Upload(new ReadOnlySpan<LightDataUBO>(ref lightData));
    }

    /// <summary>
    /// Sets up the shadow pass for a point light.
    /// </summary>
    public void SetupForPointLight(PointLight3D.Resource light)
    {
        _lightPosition = light.Position;
        _farPlane = light.Range;

        // Update the light data UBO
        UpdateLightData();

        // 90 degree FOV for cube faces
        _lightProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 2f,  // 90 degrees
            1.0f,           // Square faces
            0.1f,
            _farPlane);

        // Create view matrices for each cube face
        for (int i = 0; i < 6; i++)
        {
            var (direction, up) = s_cubeFaceDirections[i];
            var target = _lightPosition + direction;
            _lightViewMatrices[i] = Matrix4x4.CreateLookAt(_lightPosition, target, up);
        }
    }

    /// <summary>
    /// Executes the point shadow pass, rendering all 6 cube faces.
    /// </summary>
    public void Execute(IReadOnlyList<Object3D.Resource> objects)
    {
        if (RenderPass == null || _shadowPipeline == null || _descriptorSet == null)
            return;

        // Render each cube face
        for (int faceIndex = 0; faceIndex < 6; faceIndex++)
        {
            ExecuteFace(faceIndex, objects);
        }
    }

    private void ExecuteFace(int faceIndex, IReadOnlyList<Object3D.Resource> objects)
    {
        var framebuffer = _faceFramebuffers[faceIndex];
        var depthTexture = _faceDepthTextures[faceIndex];
        if (framebuffer == null || depthTexture == null || ShadowCubeTexture == null)
            return;

        // Calculate light VP for this face (View * Projection in C# System.Numerics)
        var lightVP = _lightViewMatrices[faceIndex] * _lightProjectionMatrix;

        // Begin pass with this face's framebuffer
        Span<Color> clearColors = [new Color(255, 255, 255, 255)];
        RenderPass!.Begin(framebuffer, clearColors);

        // Bind shadow pipeline
        RenderPass.BindPipeline(_shadowPipeline!);

        // Bind descriptor set with light data
        RenderPass.BindDescriptorSet(_shadowPipeline!, _descriptorSet!);

        // Render each object
        foreach (var obj in objects)
        {
            RenderObject(obj, lightVP, Matrix4x4.Identity);
        }

        RenderPass.End();

        // Copy the rendered depth to the cube map face
        Context.CopyTextureToCubeFace(depthTexture, ShadowCubeTexture, faceIndex);
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

        // Set push constants (128 bytes: Model + LightViewProjection)
        var pushConstants = new PointShadowPushConstants
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
        _descriptorSet?.Dispose();
        _lightDataBuffer?.Dispose();
        DisposeFaceResources();
        RenderPass?.Dispose();
        ShadowCubeTexture?.Dispose();
    }
}
