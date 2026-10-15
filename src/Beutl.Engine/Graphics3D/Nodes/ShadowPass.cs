using System.Numerics;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Lighting;
using Beutl.Media;

namespace Beutl.Graphics3D.Nodes;

/// <summary>
/// Shadow pass for rendering directional and spot light shadow maps.
/// </summary>
public sealed class ShadowPass : RenderNode3D
{
    /// <summary>
    /// Default shadow map resolution.
    /// </summary>
    public const int DefaultShadowMapSize = 2048;

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

    private readonly ShadowCasterPipelines _pipelines = new();
    // The dummy color attachment the framebuffer requires.
    private ITexture2D? _dummyColorTexture;
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
    /// Gets the light view matrix.
    /// </summary>
    public Matrix4x4 LightViewMatrix { get; private set; }

    /// <summary>
    /// Gets the light projection matrix.
    /// </summary>
    public Matrix4x4 LightProjectionMatrix { get; private set; }

    /// <summary>
    /// Gets the combined light view-projection matrix, View * Projection in System.Numerics order.
    /// </summary>
    public Matrix4x4 LightViewProjection => LightViewMatrix * LightProjectionMatrix;

    protected override void OnInitialize(int width, int height)
    {
        // Shadow pass uses its own fixed size
        CreateShadowMap();
        CompileShaders();
        CreatePipeline();
    }

    private void CreateShadowMap()
    {
        const int size = DefaultShadowMapSize;

        // The extent this pass allocates is the shadow map size, not the width and height Initialize was
        // handed, so the device limit is asked here, before anything is allocated.
        DeviceExtentLimits.ThrowIfCannotAttach(Context, size, size);

        ShadowDepthTexture = Context.CreateTexture2D(size, size, TextureFormat.Depth32Float);

        _dummyColorTexture = Context.CreateTexture2D(size, size, TextureFormat.R8Unorm);

        RenderPass = Context.CreateRenderPass3D(ShadowPassFormats, TextureFormat.Depth32Float);
        Framebuffer = Context.CreateFramebuffer3D(
            RenderPass,
            [_dummyColorTexture],
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

        // No descriptor bindings needed - we only use push constants
        _pipelines.Create(Context, RenderPass, _vertexShaderSpirv, _fragmentShaderSpirv, []);
    }

    /// <summary>
    /// Sets up the shadow pass for a directional light.
    /// </summary>
    public void SetupForDirectionalLight(DirectionalLight3D.Resource light, Vector3 sceneCenter)
    {
        var direction = NormalizeLightDirection(light.Direction);

        var shadowDistance = light.ShadowDistance;
        var lightPosition = sceneCenter - direction * shadowDistance * 0.5f;

        var up = ChooseUp(direction);
        LightViewMatrix = Matrix4x4.CreateLookAt(lightPosition, sceneCenter, up);

        var size = light.ShadowMapSize;
        LightProjectionMatrix = Matrix4x4.CreateOrthographic(size, size, 0.1f, shadowDistance);
    }

    /// <summary>
    /// Sets up the shadow pass for a spot light.
    /// </summary>
    public void SetupForSpotLight(SpotLight3D.Resource light)
    {
        var position = light.Position;
        var direction = NormalizeLightDirection(light.Direction);

        var up = ChooseUp(direction);
        var target = position + direction;
        LightViewMatrix = Matrix4x4.CreateLookAt(position, target, up);

        var fovRadians = light.OuterConeAngle * 2f * MathF.PI / 180f;
        fovRadians = Math.Clamp(fovRadians, 0.1f, MathF.PI - 0.1f);
        // The near plane stays in front of the far plane for short ranges.
        float nearPlane = Math.Clamp(light.Range * 0.1f, 0.001f, 1f);
        LightProjectionMatrix = Matrix4x4.CreatePerspectiveFieldOfView(
            fovRadians,
            1.0f,  // Square shadow map
            nearPlane,
            MathF.Max(light.Range, nearPlane * 2));
    }

    /// <summary>Normalizes a light direction, using <see cref="Vector3.UnitY"/> for a zero direction.</summary>
    private static Vector3 NormalizeLightDirection(Vector3 direction)
    {
        if (direction == Vector3.Zero)
            direction = Vector3.UnitY;
        return Vector3.Normalize(direction);
    }

    /// <summary>
    /// The up vector for a light view along <paramref name="direction"/>. A look-at view needs an up vector that is
    /// not parallel to the view direction, so a light looking (nearly) along Y uses Z.
    /// </summary>
    private static Vector3 ChooseUp(Vector3 direction)
    {
        return Math.Abs(Vector3.Dot(direction, Vector3.UnitY)) > 0.99f
            ? Vector3.UnitZ
            : Vector3.UnitY;
    }

    /// <summary>
    /// Executes the shadow pass for the configured light.
    /// </summary>
    public void Execute(IReadOnlyList<Object3D.Resource> objects)
    {
        if (Framebuffer == null || RenderPass == null || !_pipelines.IsCreated)
            return;

        Span<Color> clearColors = [new Color(255, 255, 255, 255)]; // Dummy color
        using (UsePass(clearColors))
        {
            _pipelines.ResetBinding();

            var lightVP = LightViewProjection;

            Object3DTraversal.ForEachEnabled(objects, (obj, worldMatrix) =>
            {
                // CastShadows is per object; enabled children still traverse a non-casting parent. A fully
                // transparent material draws nothing, so it casts nothing either.
                if (obj.CastsVisibleShadow)
                    RenderMesh(obj, lightVP, worldMatrix);
            });
        }
    }

    private void RenderMesh(Object3D.Resource obj, Matrix4x4 lightVP, Matrix4x4 worldMatrix)
    {
        var meshResource = MeshDrawHelper.Prepare(Context, obj);
        if (meshResource == null)
            return;

        _pipelines.Bind(RenderPass!, obj.Material?.IsDoubleSided == true, out _);
        ShadowCasterPipelines.SetPushConstants(RenderPass!, worldMatrix, lightVP);

        MeshDrawHelper.Draw(RenderPass!, meshResource);
    }

    protected override void OnDispose()
    {
        DisposeResources([_pipelines.OneSided, _pipelines.DoubleSided, Framebuffer,
            RenderPass, ShadowDepthTexture, _dummyColorTexture]);
    }
}
