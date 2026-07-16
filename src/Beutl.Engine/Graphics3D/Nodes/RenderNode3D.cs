using Beutl.Graphics.Backend;

namespace Beutl.Graphics3D.Nodes;

/// <summary>
/// Abstract base class for all 3D rendering nodes.
/// Provides common functionality for both render and compute operations.
/// </summary>
public abstract class RenderNode3D : IDisposable
{
    private bool _initialized;
    private bool _disposed;

    protected RenderNode3D(IGraphicsContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public bool IsDisposed => _disposed;

    public bool IsInitialized => _initialized;

    public IGraphicsContext Context { get; }

    public int Width { get; protected set; }

    public int Height { get; protected set; }

    public virtual void Initialize(int width, int height)
    {
        ThrowIfDisposed();
        if (_initialized)
            throw new InvalidOperationException($"{GetType().Name} is already initialized.");

        OnInitialize(width, height);
        Width = width;
        Height = height;
        _initialized = true;
    }

    public virtual void Resize(int width, int height)
    {
        ThrowIfNotInitialized();
        if (Width == width && Height == height)
            return;

        OnResize(width, height);
        Width = width;
        Height = height;
    }

    protected void ThrowIfDisposed()
        => ObjectDisposedException.ThrowIf(_disposed, this);

    protected void ThrowIfNotInitialized()
    {
        ThrowIfDisposed();
        if (!_initialized)
            throw new InvalidOperationException($"{GetType().Name} is not initialized.");
    }

    protected abstract void OnInitialize(int width, int height);

    protected abstract void OnResize(int width, int height);

    protected abstract void OnDispose();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _initialized = false;
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
