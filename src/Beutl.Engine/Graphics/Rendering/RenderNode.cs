using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;

namespace Beutl.Graphics.Rendering;

public abstract class RenderNode : IDisposable
{
    private long _changeVersion;
    private long _clearedVersion;

    protected RenderNode()
    {
        Cache = new RenderNodeCache(this);
    }

    public bool IsDisposed { get; private set; }

    /// <summary>Whether this node would record something other than what its last consumed recording holds.</summary>
    /// <remarks>
    /// A pair of versions rather than a stored flag, because a request has to tell the mark it observed when
    /// it began from one that arrived while it was recording. Lowering a flag would drop both, and the second
    /// mark describes content no recording has been taken of yet.
    /// </remarks>
    public bool HasChanges => _changeVersion != _clearedVersion;

    /// <summary>Reports that this node's next recording differs from the one it last had consumed.</summary>
    /// <remarks>
    /// Raising is the only direction a node gets: withdrawing a change it has already reported would let the
    /// output cached before the withdrawal be served for a state that never produced it. Lowering belongs
    /// to <see cref="ClearChanges"/>, which the engine calls with the version the recording was taken at, so
    /// a mark landing during that recording is not swallowed by its clear. Call it on the thread that records.
    /// </remarks>
    public void MarkChanged()
    {
        _changeVersion++;
    }

    internal long ChangeVersion => _changeVersion;

    /// <summary>The nodes this node records through, in recording order.</summary>
    /// <remarks>
    /// Content dependency, not ownership: a node reports what its output is built from, even nodes it never
    /// disposes. The relation must be acyclic and the span valid while iterated. A node that leaves this empty is
    /// not looked through, so nothing below it is revalidated or render-cached and it must never be cacheable.
    /// </remarks>
    public virtual ReadOnlySpan<RenderNode> ChildNodes => default;

    internal RenderNodeCache Cache { get; }

    public abstract void Process(RenderNodeContext context);

    /// <summary>Prepares this node for one request, before its children are recorded.</summary>
    /// <remarks>
    /// Recording walks children before their parent, so a node whose children depend on the request reconciles
    /// them here against <paramref name="preparation"/>. It runs once per request, before <see cref="Process"/>,
    /// however the node is reached, so an override that changes nothing must cost nothing.
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
            IsDisposed = true;
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>Reports that the recording taken at <paramref name="observedVersion"/> has been consumed.</summary>
    /// <remarks>
    /// A mark after <paramref name="observedVersion"/> describes content that recording does not hold, so
    /// <see cref="HasChanges"/> stays raised for it.
    /// </remarks>
    internal void ClearChanges(long observedVersion)
    {
        if (_changeVersion == observedVersion)
            _clearedVersion = observedVersion;
    }

    protected virtual void OnDispose(bool disposing)
    {
    }
}
