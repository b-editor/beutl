using System.Runtime.ExceptionServices;
using Beutl.Graphics.Backend;
using Beutl.Media;

namespace Beutl.Graphics3D.Nodes;

/// <summary>
/// Abstract base class for the 3D render passes: geometry, lighting, shadow and post-process.
/// </summary>
public abstract class RenderNode3D : IDisposable
{
    private bool _disposed;

    protected RenderNode3D(IGraphicsContext context, IShaderCompiler shaderCompiler)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        ShaderCompiler = shaderCompiler ?? throw new ArgumentNullException(nameof(shaderCompiler));
    }

    public IGraphicsContext Context { get; }

    protected IShaderCompiler ShaderCompiler { get; }

    public int Width { get; protected set; }

    public int Height { get; protected set; }

    /// <summary>
    /// Gets the render pass for this node.
    /// </summary>
    public IRenderPass3D? RenderPass { get; protected set; }

    /// <summary>
    /// Gets the framebuffer for this node.
    /// </summary>
    public IFramebuffer3D? Framebuffer { get; protected set; }

    /// <summary>
    /// Allocates this node's resources for an extent. A node is initialized once; a new extent takes new nodes.
    /// </summary>
    /// <remarks>
    /// A node that allocates the extent it is handed refuses one past what <see cref="Context"/> can
    /// attach before allocating, and reports it as <see cref="InvalidOperationException"/>; a node with a
    /// fixed-size allocation ignores the extent and is bounded by the context instead. The extent is
    /// committed only once the node has allocated, so a refused allocation commits none.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is zero or negative. Refused here, before the node is asked, because a node with a
    /// fixed-size allocation ignores the extent and would otherwise commit the invalid value; a positive
    /// extent is also what keeps (0, 0) meaning "none".
    /// </exception>
    public virtual void Initialize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        OnInitialize(width, height);
        Width = width;
        Height = height;
    }

    protected abstract void OnInitialize(int width, int height);

    protected abstract void OnDispose();

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
        RenderPass?.Begin(Framebuffer!, clearColors, clearDepth);
        return new PassScope(this);
    }

    /// <summary>Ends the render pass begun by <see cref="UsePass"/>.</summary>
    protected readonly ref struct PassScope(RenderNode3D owner)
    {
        public void Dispose() => owner.RenderPass?.End();
    }

    /// <summary>
    /// Prepares the framebuffer for sampling by other passes.
    /// </summary>
    public void PrepareForSampling()
    {
        Framebuffer?.PrepareForSampling();
    }

    internal static void DisposeResources(ReadOnlySpan<IDisposable?> resources)
    {
        List<Exception>? failures = null;
        foreach (IDisposable? resource in resources)
        {
            try
            {
                resource?.Dispose();
            }
            catch (Exception failure)
            {
                (failures ??= []).Add(failure);
            }
        }

        if (failures?.Count == 1)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures is not null)
            throw new AggregateException(failures);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            OnDispose();
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }
}
