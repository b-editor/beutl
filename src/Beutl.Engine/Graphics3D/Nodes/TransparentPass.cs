using System.Numerics;
using Beutl.Composition;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Meshes;
using Beutl.Media;

namespace Beutl.Graphics3D.Nodes;

/// <summary>
/// Forward rendering pass for transparent objects.
/// Draws transparent objects over the lit scene from LightingPass with alpha blending.
/// Uses the depth buffer from GeometryPass for depth testing (without writing).
/// </summary>
public sealed class TransparentPass : GraphicsNode3D
{
    private readonly ITexture2D _depthTexture;

    public TransparentPass(IGraphicsContext context, IShaderCompiler shaderCompiler, ITexture2D depthTexture)
        : base(context, shaderCompiler)
    {
        _depthTexture = depthTexture ?? throw new ArgumentNullException(nameof(depthTexture));
    }

    /// <summary>
    /// Gets the borrowed color texture containing the scene with transparent objects.
    /// </summary>
    public ITexture2D? OutputTexture { get; private set; }

    /// <summary>
    /// Sets the color texture from LightingPass as the background for transparent rendering.
    /// </summary>
    /// <param name="colorTexture">The color texture from LightingPass.</param>
    public void SetColorTexture(ITexture2D colorTexture)
    {
        if (ReferenceEquals(OutputTexture, colorTexture))
            return;

        IFramebuffer3D? oldFramebuffer = Framebuffer;
        Framebuffer = RenderPass is { } pass ? Context.CreateFramebuffer3D(pass, [colorTexture], _depthTexture) : null;
        OutputTexture = colorTexture;
        oldFramebuffer?.Dispose();
    }

    protected override void OnInitialize(int width, int height)
    {
        CreateResources(width, height);
    }

    protected override void OnResize(int width, int height)
    {
        CreateResources(width, height);
    }

    private void CreateResources(int width, int height)
    {
        DeviceExtentLimits.ThrowIfCannotAttach(Context, width, height);
        BeginReplacingResources();

        Framebuffer?.Dispose();
        Framebuffer = null;
        RenderPass?.Dispose();

        RenderPass = CreateAttachmentPass(
            [TextureFormat.RGBA16Float],
            TextureFormat.Depth32Float,
            _depthTexture.SampleCount,
            AttachmentLoadOp.Load,  // Preserve color from LightingPass
            AttachmentLoadOp.Load); // Preserve depth from GeometryPass

        if (OutputTexture is { } color)
            Framebuffer = Context.CreateFramebuffer3D(RenderPass, [color], _depthTexture);
    }

    /// <summary>
    /// Executes the transparent pass, rendering transparent objects over the lit scene.
    /// </summary>
    /// <param name="compositionContext">The 2D render context (for texture rendering).</param>
    /// <param name="camera">The camera resource.</param>
    /// <param name="transparentObjects">List of transparent objects sorted by distance (far to near).</param>
    /// <param name="lights">The list of lights in the scene.</param>
    /// <param name="ambientColor">The ambient light color.</param>
    /// <param name="ambientIntensity">The ambient light intensity.</param>
    /// <param name="aspectRatio">The aspect ratio of the viewport.</param>
    public void Execute(
        CompositionContext compositionContext,
        Camera.Camera3D.Resource camera,
        IReadOnlyList<TransparentObjectEntry> transparentObjects,
        IReadOnlyList<LightData> lights,
        Color ambientColor,
        float ambientIntensity,
        float aspectRatio,
        float surfaceDensity = 1f)
    {
        if (Framebuffer == null || RenderPass == null || transparentObjects.Count == 0)
            return;

        var viewMatrix = camera.GetViewMatrix();
        var projectionMatrix = camera.GetProjectionMatrix(aspectRatio);

        var context3D = new RenderContext3D(
            Context,
            RenderPass,
            ShaderCompiler,
            viewMatrix,
            projectionMatrix,
            camera.Position,
            ambientColor.ToLinearPremultiplied().AsVector3() * ambientIntensity,
            lights,
            compositionContext,
            surfaceDensity);

        Span<Color> clearColors = [Colors.Transparent];
        using (UsePass(clearColors))
        {
            // Already sorted far to near.
            foreach (var entry in transparentObjects)
            {
                MeshDrawHelper.DrawWithMaterial(context3D, entry.Object, entry.WorldMatrix, null);
            }
        }
    }

    protected override void OnDispose()
    {
        DisposeResources([Framebuffer, RenderPass]);
    }
}
