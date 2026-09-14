using Beutl.Graphics.Backend;

namespace Beutl.Graphics3D.Nodes;

/// <summary>
/// Abstract base class for all 3D rendering nodes.
/// Provides common functionality for both render and compute operations.
/// </summary>
public abstract class RenderNode3D : IDisposable
{
    private bool _disposed;

    protected RenderNode3D(IGraphicsContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public bool IsDisposed => _disposed;

    public IGraphicsContext Context { get; }

    public int Width { get; protected set; }

    public int Height { get; protected set; }

    /// <summary>
    /// Allocates this node's resources for an extent.
    /// </summary>
    /// <remarks>
    /// A node that allocates the extent it is handed refuses one past what <see cref="Context"/> can
    /// attach before allocating, and reports it as <see cref="InvalidOperationException"/>; a node with a
    /// fixed-size allocation ignores the extent and is bounded by the context instead. The extent is
    /// committed only once the node has allocated, so a refused or failed allocation is retryable.
    /// </remarks>
    public virtual void Initialize(int width, int height)
    {
        OnInitialize(width, height);
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Reallocates this node's resources for a new extent.
    /// </summary>
    /// <remarks>
    /// The extent is committed only once the node has reallocated, so a refused resize keeps the extent
    /// its resources still describe and a later request for a different size is not mistaken for a no-op.
    /// </remarks>
    public virtual void Resize(int width, int height)
    {
        if (Width == width && Height == height)
            return;

        OnResize(width, height);
        Width = width;
        Height = height;
    }

    protected abstract void OnInitialize(int width, int height);

    protected abstract void OnResize(int width, int height);

    protected abstract void OnDispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        OnDispose();
        GC.SuppressFinalize(this);
    }
}
