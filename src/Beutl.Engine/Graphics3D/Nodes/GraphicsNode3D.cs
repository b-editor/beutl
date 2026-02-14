using Beutl.Graphics.Backend;
using Beutl.Media;

namespace Beutl.Graphics3D.Nodes;

/// <summary>
/// Abstract base class for render pass nodes.
/// Provides common functionality for geometry, lighting, shadow, and post-process passes.
/// </summary>
public abstract class GraphicsNode3D : RenderNode3D
{
    protected GraphicsNode3D(IGraphicsContext context, IShaderCompiler shaderCompiler)
        : base(context)
    {
        ShaderCompiler = shaderCompiler ?? throw new ArgumentNullException(nameof(shaderCompiler));
    }

    protected IShaderCompiler ShaderCompiler { get; }

    /// <summary>
    /// Gets the render pass for this node.
    /// </summary>
    public IRenderPass3D? RenderPass { get; protected set; }

    /// <summary>
    /// Gets the framebuffer for this node.
    /// </summary>
    public IFramebuffer3D? Framebuffer { get; protected set; }

    /// <summary>
    /// Begins the render pass with the specified clear colors.
    /// </summary>
    protected void BeginPass(Span<Color> clearColors, float clearDepth = 1.0f)
    {
        RenderPass?.Begin(Framebuffer!, clearColors, clearDepth);
    }

    /// <summary>
    /// Ends the render pass.
    /// </summary>
    protected void EndPass()
    {
        RenderPass?.End();
    }

    /// <summary>
    /// Prepares the framebuffer for sampling by other passes.
    /// </summary>
    public void PrepareForSampling()
    {
        Framebuffer?.PrepareForSampling();
    }
}
