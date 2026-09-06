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
    private ITexture2D? _colorInput;

    public TransparentPass(IGraphicsContext context, IShaderCompiler shaderCompiler, ITexture2D depthTexture)
        : base(context, shaderCompiler)
    {
        _depthTexture = depthTexture ?? throw new ArgumentNullException(nameof(depthTexture));
    }

    /// <summary>
    /// Gets the output texture containing the scene with transparent objects.
    /// </summary>
    public ITexture2D? OutputTexture { get; private set; }

    /// <summary>
    /// Sets the color texture from LightingPass as the background for transparent rendering.
    /// </summary>
    /// <param name="colorTexture">The color texture from LightingPass.</param>
    public void SetColorTexture(ITexture2D colorTexture)
    {
        _colorInput = colorTexture;
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
        // Dispose old resources
        Framebuffer?.Dispose();
        RenderPass?.Dispose();
        OutputTexture?.Dispose();

        // Create output texture (same format as LightingPass output)
        OutputTexture = Context.CreateTexture2D(width, height, TextureFormat.RGBA8Unorm);

        // Create render pass with Load operation to preserve existing content
        RenderPass = Context.CreateRenderPass3D(
            [TextureFormat.RGBA8Unorm],
            TextureFormat.Depth32Float,
            AttachmentLoadOp.Load,  // Preserve color from LightingPass
            AttachmentLoadOp.Load); // Preserve depth from GeometryPass

        // Create framebuffer using output texture and the shared depth texture
        Framebuffer = Context.CreateFramebuffer3D(
            RenderPass,
            [OutputTexture],
            _depthTexture);
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
        if (Framebuffer == null || RenderPass == null || OutputTexture == null || _colorInput == null)
            return;

        // First, copy the LightingPass output to our output texture
        Context.CopyTexture(_colorInput, OutputTexture);

        if (transparentObjects.Count == 0)
            return;

        // Create RenderContext3D for transparent materials
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

        // Begin transparent pass with load (preserves copied content and depth buffer)
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
        Framebuffer?.Dispose();
        RenderPass?.Dispose();
        OutputTexture?.Dispose();
    }
}
