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
}
