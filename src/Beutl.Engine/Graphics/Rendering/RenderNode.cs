using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering;

public abstract class RenderNode : INode
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
    /// How this node's buffer-allocating boundary derives its working scale from its
    /// inputs' supply densities and the render request's output scale (feature 003).
    /// Defaults to <see cref="ResolutionPolicy.Inherit"/> (supply-driven).
    /// </summary>
    public virtual ResolutionPolicy ResolutionPolicy => ResolutionPolicy.Inherit;

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
