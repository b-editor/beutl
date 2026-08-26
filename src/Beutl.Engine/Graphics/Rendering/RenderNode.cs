using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering;

public abstract class RenderNode : IDisposable
{
    private long _changeVersion;
    private long _clearedVersion;

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
    /// <remarks>
    /// Derived rather than stored, because <see cref="MarkChanged"/> is public and reaches this node from
    /// whatever thread owns its content while the render thread completes a request. A stored flag would have
    /// to be lowered by a check-then-act that a concurrent mark can slip through; a version the mark raises
    /// and the clear can only match cannot lose that mark whichever order the two land in.
    /// </remarks>
    public bool HasChanges => Volatile.Read(ref _changeVersion) != Volatile.Read(ref _clearedVersion);

    /// <summary>Reports that this node's next recording differs from the one it last had consumed.</summary>
    /// <remarks>
    /// Raising is the only direction a node gets: withdrawing a change it has already reported would let the
    /// recording taken before the withdrawal be replayed for a state that never produced it. Lowering the
    /// flag belongs to <see cref="ClearChanges"/>, which the engine calls with the version it recorded at, so
    /// a mark that lands while a recording is in flight is not swallowed by that recording's clear. Callable
    /// from any thread, including while a request is being completed elsewhere.
    /// </remarks>
    public void MarkChanged()
    {
        // Interlocked, not ++: two owners marking at once must not collapse into one version, or the second
        // mark is left at a version a clear already in flight is entitled to consume.
        Interlocked.Increment(ref _changeVersion);
    }

    internal long ChangeVersion => Volatile.Read(ref _changeVersion);

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

    /// <summary>Reports that the recording taken at <paramref name="observedVersion"/> has been consumed.</summary>
    /// <remarks>
    /// A mark landing between the two steps below raises the version past <paramref name="observedVersion"/>,
    /// so the write can no longer describe the node's current content and <see cref="HasChanges"/> stays
    /// raised for it. The stamp only ever moves forward, so two requests completing at once cannot walk it
    /// back onto a recording that has already been superseded.
    /// </remarks>
    internal void ClearChanges(long observedVersion)
    {
        if (Volatile.Read(ref _changeVersion) != observedVersion)
            return;

        long cleared;
        do
        {
            cleared = Volatile.Read(ref _clearedVersion);
            if (cleared >= observedVersion)
                return;
        }
        while (Interlocked.CompareExchange(ref _clearedVersion, observedVersion, cleared) != cleared);
    }

    protected virtual void OnDispose(bool disposing)
    {
    }
}
