namespace Beutl.Graphics.Rendering;

/// <summary>
/// The single-entry compiled-plan cache a <see cref="PlanFilterEffectRenderNode"/> holds (feature 004, T034,
/// contracts/execution-plan.md C5, data-model §3). A render node has one effect structure at a time, so caching
/// the last plan is all that ever helps. The cached plan is reused iff <b>both</b> the <see cref="StructuralKey"/>
/// and the graphics-context identity match; on a hit the frame rebinds parameters (<see cref="ParameterBlock"/>)
/// without a recompile.
/// </summary>
/// <remarks>
/// Exhaustive invalidation (C5): a structural-key mismatch, a graphics-context change (device loss/recreation),
/// and node dispose. <b>Bounds, ROIs, buffer sizes, and the resolved working scale are never invalidation
/// triggers</b> — they are per-frame resource-resolution inputs, so an animated blur sigma or drop-shadow offset
/// re-resolves sizes on a cache hit. The cached plan owns no GPU-side resources (intermediate targets are
/// frame-scoped and returned to the pool as their ops dispose; shader programs live in the shared
/// <see cref="ProgramCache"/>), so invalidation only drops the reference.
/// </remarks>
internal sealed class PlanCache
{
    private bool _hasEntry;
    private StructuralKey _key;
    private object? _contextId;
    private CompiledPlan? _plan;

    /// <summary>
    /// Returns the cached plan when its structural key and graphics context both match, else <see langword="false"/>
    /// (the caller compiles and <see cref="Store"/>s). A context change is a reference change of
    /// <paramref name="contextId"/> — a recreated device never satisfies the same-context half of C5.
    /// </summary>
    public bool TryGet(in StructuralKey key, object contextId, out CompiledPlan plan)
    {
        if (_hasEntry && _plan is not null && ReferenceEquals(_contextId, contextId) && _key == key)
        {
            plan = _plan;
            return true;
        }

        plan = null!;
        return false;
    }

    /// <summary>Replaces the entry after a compile (a real recompile invalidated any previous plan).</summary>
    public void Store(in StructuralKey key, object contextId, CompiledPlan plan)
    {
        Invalidate();
        _key = key;
        _contextId = contextId;
        _plan = plan;
        _hasEntry = true;
    }

    /// <summary>Drops the cached plan (key mismatch, context change, or node dispose).</summary>
    public void Invalidate()
    {
        _hasEntry = false;
        _contextId = null;
        _plan = null;
    }
}
