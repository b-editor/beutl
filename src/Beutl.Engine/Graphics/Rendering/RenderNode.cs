using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering;

public abstract class RenderNode : IDisposable
{
    private bool _hasChanges;
    private long _changeVersion;

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

    public bool HasChanges
    {
        get => _hasChanges;
        set
        {
            _hasChanges = value;
            if (value)
            {
                _changeVersion++;
            }
        }
    }

    internal long ChangeVersion => _changeVersion;

    /// <summary>The nodes this node records through, in recording order.</summary>
    /// <remarks>
    /// Content dependency, not ownership: a node that only references another node and never disposes it
    /// still reports it here, because revalidation and cache validity follow what a node's output is built
    /// from. Disposal and cache teardown follow ownership instead. The relation must be acyclic and the
    /// span must stay valid while a caller iterates it. A node that discovers what it records through only
    /// while processing, and so cannot hold a stable span, leaves this empty: both traversals then stop at
    /// it, so nothing below it is revalidated or render-cached and the node itself must never be cacheable.
    /// </remarks>
    public virtual ReadOnlySpan<RenderNode> ChildNodes => default;

    internal RenderNodeCache Cache { get; }

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

    internal void ClearChanges(long observedVersion)
    {
        if (_hasChanges && _changeVersion == observedVersion)
        {
            _hasChanges = false;
        }
    }

    protected virtual void OnDispose(bool disposing)
    {
    }
}
