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
    /// Begins the render pass and returns a scope that ends it on every path out of the body.
    /// </summary>
    /// <remarks>
    /// The pass records into the context-wide batch and holds a render-pass scope on it, so a body that
    /// throws would otherwise leave the batch with an unterminated render pass and divert every later
    /// transfer in the process to its own submission.
    /// </remarks>
    protected PassScope UsePass(scoped Span<Color> clearColors, float clearDepth = 1.0f)
    {
        BeginPass(clearColors, clearDepth);
        return new PassScope(this);
    }

    /// <summary>Ends the render pass begun by <see cref="UsePass"/>.</summary>
    protected readonly ref struct PassScope(GraphicsNode3D owner)
    {
        public void Dispose() => owner.EndPass();
    }

    /// <summary>
    /// Prepares the framebuffer for sampling by other passes.
    /// </summary>
    public void PrepareForSampling()
    {
        Framebuffer?.PrepareForSampling();
    }
}
