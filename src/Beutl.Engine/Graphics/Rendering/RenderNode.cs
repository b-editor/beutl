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

    /// <summary>
    /// Called when this node's output will be served from a render-node cache — its own or an ancestor's — so
    /// <see cref="Process"/> will not run on subsequent frames. Overriders must release any cross-frame resources
    /// they hold outside that node cache (e.g. a retained pooled lease) so it is not stranded until node dispose.
    /// The default is a no-op; a later cache invalidation re-runs <see cref="Process"/>, which re-acquires as needed.
    /// </summary>
    protected internal virtual void OnServedFromCache()
    {
    }
}
