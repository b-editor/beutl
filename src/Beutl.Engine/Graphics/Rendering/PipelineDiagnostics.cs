namespace Beutl.Graphics.Rendering;

/// <summary>
/// Per-renderer effect-pipeline counters (feature 004, contracts/execution-plan.md §C8). Each counter is a
/// plain <see cref="long"/> incremented on the render thread with no locks; "not observed" costs nothing
/// beyond a null check at the call site. One instance is owned per <see cref="RenderNodeProcessor"/> and
/// seeded into every <see cref="RenderNodeContext"/> it pulls. Read via <see cref="Snapshot"/> in tests.
/// Mutation (the <c>internal set</c> accessors and <see cref="Reset"/>) is confined to the engine: the counters
/// are handed to public authoring callbacks (<c>GeometrySession.Diagnostics</c> / <c>PassUniformContext.Diagnostics</c>)
/// for OBSERVATION only, so plugin code can read but never forge or reset them (keeps <c>IRenderer.Diagnostics</c> trustworthy).
/// </summary>
public sealed class PipelineDiagnostics
{
    /// <summary>
    /// C8: each executed draw/dispatch of a pass — a fused group counts one, K compute iterations count K,
    /// each split branch and each composite fan-in draw counts one.
    /// </summary>
    public long GpuPasses { get; internal set; }

    /// <summary>
    /// C8: each fresh GPU target creation (pool miss or non-pooled) on an effect pass's buffer acquire. Non-effect
    /// surfaces (the root render target, per-operation rasterization) are deliberately not counted.
    /// </summary>
    public long TargetAllocations { get; internal set; }

    /// <summary>C8: each <see cref="RenderTargetPool"/> acquire (hit or miss).</summary>
    public long PoolAcquires { get; internal set; }

    /// <summary>C8: each pool acquire that had to allocate (a miss).</summary>
    public long PoolMisses { get; internal set; }

    /// <summary>
    /// C8: each bake of an upstream operation into a pooled buffer so a geometry/compute/split pass can sample it as
    /// a texture (the plan executor's input materialization).
    /// </summary>
    public long FullFrameMaterializations { get; internal set; }

    /// <summary>
    /// C8: each schedule-level backend transition (C4.2, a pass whose <c>SyncBefore</c> is set). Counted by the
    /// plan executor, not inside <see cref="RenderTarget"/>, because <see cref="RenderTarget.PrepareForSampling"/> /
    /// <see cref="RenderTarget.BeginDraw"/> also fire for non-effect surfaces (root draw, snapshot readback) and
    /// for the per-draw flushes Skia itself performs inside a pass.
    /// </summary>
    public long FlushSyncs { get; internal set; }

    /// <summary>C8: each effect-graph compile (a <see cref="PlanCache"/> hit rebinds without counting).</summary>
    public long PlanCompilations { get; internal set; }

    /// <summary>C8: each <c>SKRuntimeEffect</c> / Vulkan pipeline construction (a <see cref="ProgramCache"/> miss).</summary>
    public long ProgramCreations { get; internal set; }

    /// <summary>
    /// C8/C10: each frame a stable effect-chain prefix is reused instead of re-executed — one increment per frame the
    /// pass-prefix output cache engages, so the skipped passes' <see cref="GpuPasses"/> and allocations do not occur
    /// (contracts/execution-plan.md §C10).
    /// </summary>
    public long PrefixCacheHits { get; internal set; }

    /// <summary>
    /// C8/C9: each composite branch drawn through a full-canvas <c>SaveLayer</c>. Only blend modes that do not
    /// preserve the destination under a transparent source need one; a src-over branch draws directly (or through
    /// a branch-bounded layer when a C9 color-filter fold applies), so src-over composites keep this at zero.
    /// </summary>
    public long CompositeLayerSaves { get; internal set; }

    /// <summary>Takes an immutable copy of the current counter values for test assertions.</summary>
    public PipelineDiagnosticsSnapshot Snapshot() => new(
        GpuPasses,
        TargetAllocations,
        PoolAcquires,
        PoolMisses,
        FullFrameMaterializations,
        FlushSyncs,
        PlanCompilations,
        ProgramCreations,
        PrefixCacheHits,
        CompositeLayerSaves);

    /// <summary>Resets every counter to zero. Engine-internal (test API); never exposed to authoring callbacks.</summary>
    internal void Reset()
    {
        GpuPasses = 0;
        TargetAllocations = 0;
        PoolAcquires = 0;
        PoolMisses = 0;
        FullFrameMaterializations = 0;
        FlushSyncs = 0;
        PlanCompilations = 0;
        ProgramCreations = 0;
        PrefixCacheHits = 0;
        CompositeLayerSaves = 0;
    }
}

/// <summary>Immutable snapshot of <see cref="PipelineDiagnostics"/> counters (contracts/execution-plan.md §C8).</summary>
public readonly record struct PipelineDiagnosticsSnapshot(
    long GpuPasses,
    long TargetAllocations,
    long PoolAcquires,
    long PoolMisses,
    long FullFrameMaterializations,
    long FlushSyncs,
    long PlanCompilations,
    long ProgramCreations,
    long PrefixCacheHits,
    long CompositeLayerSaves);
