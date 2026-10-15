using System.Numerics;
using Beutl.Composition;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Materials;
using Beutl.Media;

namespace Beutl.Graphics3D.Nodes;

/// <summary>
/// Geometry pass for deferred rendering.
/// Renders objects to the G-Buffer (Position, Normal+Metallic, Albedo+Roughness, Emission+AO, Depth).
/// </summary>
public sealed class GeometryPass : RenderNode3D
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
    /// Gets the world position texture (RGBA16F).
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
        _defaultMaterialResource = _defaultMaterial.ToResource(new CompositionContext(TimeSpan.Zero));
    }

    private void CreateGBuffer(int width, int height)
    {
        // This pass allocates the extent it is handed, so the device limit is asked here, before anything
        // is allocated.
        DeviceExtentLimits.ThrowIfCannotAttach(Context, width, height);

        PositionTexture = Context.CreateTexture2D(width, height, TextureFormat.RGBA16Float);
        NormalMetallicTexture = Context.CreateTexture2D(width, height, TextureFormat.RGBA16Float);
        AlbedoRoughnessTexture = Context.CreateTexture2D(width, height, TextureFormat.RGBA8Unorm);
        EmissionAOTexture = Context.CreateTexture2D(width, height, TextureFormat.RGBA16Float);
        DepthTexture = Context.CreateTexture2D(width, height, TextureFormat.Depth32Float);

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
        Camera.Camera3D.Resource camera,
        IReadOnlyList<Object3D.Resource> objects,
        float aspectRatio,
        IReadOnlyList<LightData> lightDataList,
        Color ambientColor,
        float ambientIntensity,
        float surfaceDensity = 1f)
    {
        if (Framebuffer == null || RenderPass == null)
            return;

        var renderContext3D = new RenderContext3D(
            Context,
            RenderPass,
            ShaderCompiler,
            camera.GetViewMatrix(),
            camera.GetProjectionMatrix(aspectRatio),
            camera.Position,
            ambientColor.ToLinearPremultiplied().AsVector3() * ambientIntensity,
            lightDataList,
            surfaceDensity);

        // Clear values; LightingPass discards the background, marked by position alpha 0.
        Span<Color> clearColors =
        [
            new Color(0, 0, 0, 0),       // Position (black, alpha=0 for invalid background)
            new Color(255, 128, 128, 0), // Normal (0.5,0.5,0 = (0,0,-1)) + Metallic=1
            new Color(0, 0, 0, 0),       // Albedo (black) + Roughness=0
            new Color(255, 0, 0, 0)      // Emission (none) + AO=1
        ];

        using (UsePass(clearColors))
        {
            Object3DTraversal.ForEachEnabled(objects, (obj, worldMatrix) =>
            {
                if (obj.Material?.IsTransparent != true)
                    MeshDrawHelper.DrawWithMaterial(renderContext3D, obj, worldMatrix, _defaultMaterialResource);
            });
        }
    }

    protected override void OnDispose()
    {
        DisposeResources([_defaultMaterialResource, Framebuffer, RenderPass, PositionTexture,
            NormalMetallicTexture, AlbedoRoughnessTexture, EmissionAOTexture, DepthTexture]);
    }
}
