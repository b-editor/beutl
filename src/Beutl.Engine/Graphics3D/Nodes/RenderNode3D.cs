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
    /// <exception cref="InvalidOperationException">
    /// The extent exceeds what <see cref="Context"/> can attach. The refusal is raised before any
    /// allocation, because the driver would not raise it: it either builds an attachment past its own limit
    /// or aborts the process.
    /// </exception>
    public virtual void Initialize(int width, int height)
    {
        DeviceExtentLimits.ThrowIfCannotAttach(Context, width, height);

        Width = width;
        Height = height;
        OnInitialize(width, height);
    }

    /// <summary>
    /// Reallocates this node's resources for a new extent.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The extent exceeds what <see cref="Context"/> can attach. The current extent and its resources are
    /// left as they were.
    /// </exception>
    public virtual void Resize(int width, int height)
    {
        if (Width == width && Height == height)
            return;

        DeviceExtentLimits.ThrowIfCannotAttach(Context, width, height);

        Width = width;
        Height = height;
        OnResize(width, height);
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
