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

    /// <summary>Whether this node would record something other than what its last consumed recording holds.</summary>
    public bool HasChanges => _hasChanges;

    /// <summary>Reports that this node's next recording differs from the one it last had consumed.</summary>
    /// <remarks>
    /// Raising is the only direction a node gets: withdrawing a change it has already reported would let the
    /// recording taken before the withdrawal be replayed for a state that never produced it. Lowering the
    /// flag belongs to <see cref="ClearChanges"/>, which the engine calls with the version it recorded at, so
    /// a mark that lands while a recording is in flight is not swallowed by that recording's clear.
    /// </remarks>
    public void MarkChanged()
    {
        _hasChanges = true;
        _changeVersion++;
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

    /// <summary>What this node recorded for the previous request, when that recording can stand for another.</summary>
    /// <remarks>
    /// One slot, not a table keyed by request: a node records once for the request it is walked in, and a
    /// second request that disagrees with <see cref="RenderNodeRecordingKey"/> replaces the entry rather than
    /// competing with it.
    /// </remarks>
    internal RenderNodeRecordingSnapshot? RecordingSnapshot { get; set; }

    public abstract void Process(RenderNodeContext context);

    /// <summary>Prepares this node for one request, before its children are recorded.</summary>
    /// <remarks>
    /// Recording walks children before their parent, so a node whose children depend on the request - one
    /// that records a nested graph at the request's density, say - cannot rebuild them from
    /// <see cref="Process"/>: they are already recorded by then. Override this to reconcile them against
    /// <paramref name="preparation"/> first. It runs before <see cref="Process"/> on every request, however
    /// the node is reached - walked as part of a subtree, or recorded with explicit inputs - so an override
    /// that changes nothing must cost nothing.
    /// </remarks>
    public virtual void PrepareForRequest(RenderNodePreparation preparation)
    {
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            OnDispose(true);
            Cache.Dispose();
            RecordingSnapshot = null;
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
