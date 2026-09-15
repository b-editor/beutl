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
    /// <exception cref="ArgumentOutOfRangeException">
    /// A dimension is zero or negative. Refused here, before the node is asked, because a node with a
    /// fixed-size allocation ignores the extent and would otherwise commit the invalid value; a positive
    /// extent is also what keeps (0, 0) meaning "none".
    /// </exception>
    public virtual void Initialize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

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
    /// A node that has to dispose those resources before it can replace them calls
    /// <see cref="BeginReplacingResources"/> first, so a replacement that fails part-way leaves no extent
    /// behind for the next request to be mistaken for.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is zero or negative.</exception>
    public virtual void Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (Width == width && Height == height)
            return;

        OnResize(width, height);
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Forgets the extent this node holds resources for, ahead of disposing them to make room for new ones.
    /// </summary>
    /// <remarks>
    /// Call it after any refusal that leaves the old resources intact and before the first dispose. Until
    /// <see cref="Initialize"/> or <see cref="Resize"/> commits the new extent the node reports none, so a
    /// replacement that throws after disposing is not skipped as a no-op when the old size is asked for
    /// again, which would leave disposed resources in use. (0, 0) cannot collide with a held extent,
    /// since <see cref="Initialize"/> and <see cref="Resize"/> accept only positive ones.
    /// </remarks>
    protected void BeginReplacingResources()
    {
        Width = 0;
        Height = 0;
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
