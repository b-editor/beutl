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

    /// <summary>The nodes this node records through, in recording order.</summary>
    /// <remarks>
    /// Content dependency, not ownership: a node that only references another node and never disposes it
    /// still reports it here, because revalidation and cache validity follow what a node's output is built
    /// from. Disposal and cache teardown follow ownership instead. The relation must be acyclic and the
    /// span must stay valid while a caller iterates it.
    /// </remarks>
    public virtual ReadOnlySpan<RenderNode> ChildNodes => default;

    public RenderNodeCache Cache { get; }

    public abstract void Process(RenderNodeContext context);

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
