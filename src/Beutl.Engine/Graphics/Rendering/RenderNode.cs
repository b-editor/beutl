using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering;

public abstract class RenderNode : IDisposable
{
    protected RenderNode()
    {
        Cache = new RenderNodeCache(this);
    }

    ~RenderNode()
    {
        if (!IsDisposed)
        {
            OnDispose(false);
            IsDisposed = true;
        }
    }

    public bool IsDisposed { get; private set; }

    public bool HasChanges { get; set; }

    public RenderNodeCache Cache { get; }

    /// <summary>
    /// Runs before <see cref="Process"/> with the canvas the pass is compositing onto, for a node
    /// whose output depends on that canvas: a filter effect rasterizes its inputs during processing,
    /// so an operation returned by <see cref="Process"/> can be drawn before an earlier sibling's.
    /// </summary>
    /// <remarks>
    /// Called on the root only, so a node that owns or references other nodes must forward it — see
    /// <see cref="ContainerRenderNode"/> and <see cref="ReferencesChildRenderNode"/>.
    /// </remarks>
    public virtual void PrepareForProcess(ImmediateCanvas canvas)
    {
    }

    public abstract RenderNodeOperation[] Process(RenderNodeContext context);

    public void Dispose()
    {
        if (!IsDisposed)
        {
            OnDispose(true);
            Cache.Dispose();
            IsDisposed = true;
            GC.SuppressFinalize(this);
        }
    }

    protected virtual void OnDispose(bool disposing)
    {
    }
}
