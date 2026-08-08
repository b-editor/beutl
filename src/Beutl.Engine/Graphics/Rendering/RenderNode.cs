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
    /// Runs on every node of the tree before <see cref="Process"/>, with the canvas the pass is
    /// compositing onto. A node whose output depends on that canvas has to read it here: processing
    /// rasterizes filter-effect inputs on the spot, so an operation returned by <see cref="Process"/>
    /// can be drawn before an earlier sibling's operation has run.
    /// </summary>
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
