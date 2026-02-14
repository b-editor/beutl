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

    public virtual void Initialize(int width, int height)
    {
        Width = width;
        Height = height;
        OnInitialize(width, height);
    }

    public virtual void Resize(int width, int height)
    {
        if (Width == width && Height == height)
            return;

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
