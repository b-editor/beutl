using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Materials;
using Beutl.Media;

namespace Beutl.Graphics3D.Nodes;

/// <summary>
/// Shadow pass for rendering point light shadow cube maps.
/// Renders 6 faces for omnidirectional shadows.
/// </summary>
public sealed class PointShadowPass : RenderNode3D
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

    private readonly ShadowCasterPipelines _pipelines = new();
    private IDescriptorSet? _descriptorSet;
    private IDescriptorSet? _doubleSidedDescriptorSet;
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
    /// Gets the depth textures for each cube face (for copying to cube array).
    /// </summary>
    internal IReadOnlyList<ITexture2D?> FaceDepthTextures => _faceDepthTextures;

    protected override void OnInitialize(int width, int height)
    {
        CreateFaceResources();
        CompileShaders();
        CreatePipeline();
        CreateLightDataBuffer();
    }

    private void CreateFaceResources()
    {
        const int faceSize = DefaultCubeFaceSize;

        // The extent this pass allocates is the face size, not the width and height Initialize was handed,
        // so the device limit is asked here, before anything is allocated. Each face is drawn into a 2D
        // attachment and copied into a face of the shadow manager's cube array, so it has to fit the cube
        // limit and the attachment limit both.
        DeviceExtentLimits.ThrowIfCannotAttachCubeFaces(Context, faceSize);

        RenderPass = Context.CreateRenderPass3D(s_shadowPassFormats, TextureFormat.Depth32Float);

        for (int i = 0; i < 6; i++)
        {
            _faceDummyTextures[i] = Context.CreateTexture2D(faceSize, faceSize, TextureFormat.R8Unorm);

            _faceDepthTextures[i] = Context.CreateTexture2D(faceSize, faceSize, TextureFormat.Depth32Float);

            _faceFramebuffers[i] = Context.CreateFramebuffer3D(
                RenderPass,
                [_faceDummyTextures[i]!],
                _faceDepthTextures[i]!);
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

        var descriptorBindings = new DescriptorBinding[]
        {
            new(0, DescriptorType.UniformBuffer, 1, ShaderStage.Fragment)
        };
        _pipelines.Create(Context, RenderPass, _vertexShaderSpirv, _fragmentShaderSpirv, descriptorBindings);

        var poolSizes = new DescriptorPoolSize[]
        {
            new(DescriptorType.UniformBuffer, 1)
        };
        _descriptorSet = Context.CreateDescriptorSet(_pipelines.OneSided!, poolSizes);
        _doubleSidedDescriptorSet = Context.CreateDescriptorSet(_pipelines.DoubleSided!, poolSizes);
    }

    private void CreateLightDataBuffer()
    {
        _lightDataBuffer = MaterialGpuResources.CreateUniformBuffer<LightDataUBO>(Context);

        _descriptorSet?.UpdateBuffer(0, _lightDataBuffer);
        _doubleSidedDescriptorSet?.UpdateBuffer(0, _lightDataBuffer);
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

        UpdateLightData();

        _lightProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(
            MathF.PI / 2f,  // 90 degrees
            1.0f,           // Square faces
            0.1f,
            _farPlane);

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
        if (RenderPass == null
            || !_pipelines.IsCreated
            || _descriptorSet == null
            || _doubleSidedDescriptorSet == null)
        {
            return;
        }

        for (int faceIndex = 0; faceIndex < 6; faceIndex++)
        {
            ExecuteFace(faceIndex, objects);
        }
    }

    private void ExecuteFace(int faceIndex, IReadOnlyList<Object3D.Resource> objects)
    {
        var framebuffer = _faceFramebuffers[faceIndex];
        var depthTexture = _faceDepthTextures[faceIndex];
        if (framebuffer == null || depthTexture == null)
            return;

        var lightVP = _lightViewMatrices[faceIndex] * _lightProjectionMatrix;

        Span<Color> clearColors = [new Color(255, 255, 255, 255)];
        RenderPass!.Begin(framebuffer, clearColors);
        try
        {
            _pipelines.ResetBinding();

            Object3DTraversal.ForEachEnabled(objects, (obj, worldMatrix) =>
            {
                // CastShadows is per object; enabled children still traverse a non-casting parent. A fully
                // transparent material draws nothing, so it casts nothing either.
                if (obj.CastsVisibleShadow)
                    RenderMesh(obj, lightVP, worldMatrix);
            });
        }
        finally
        {
            RenderPass.End();
        }
    }

    private void RenderMesh(Object3D.Resource obj, Matrix4x4 lightVP, Matrix4x4 worldMatrix)
    {
        var meshResource = MeshDrawHelper.Prepare(Context, obj);
        if (meshResource == null)
            return;

        bool doubleSided = obj.Material?.IsDoubleSided == true;
        if (_pipelines.Bind(RenderPass!, doubleSided, out IPipeline3D pipeline))
            RenderPass!.BindDescriptorSet(pipeline, doubleSided ? _doubleSidedDescriptorSet! : _descriptorSet!);

        ShadowCasterPipelines.SetPushConstants(RenderPass!, worldMatrix, lightVP);

        MeshDrawHelper.Draw(RenderPass!, meshResource);
    }

    protected override void OnDispose()
    {
        DisposeResources([_pipelines.OneSided, _pipelines.DoubleSided, _descriptorSet,
            _doubleSidedDescriptorSet, _lightDataBuffer, .. _faceFramebuffers,
            .. _faceDummyTextures, .. _faceDepthTextures, RenderPass]);
    }
}
