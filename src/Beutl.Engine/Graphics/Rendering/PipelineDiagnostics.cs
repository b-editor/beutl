namespace Beutl.Graphics.Rendering;

/// <summary>
/// Per-renderer effect-pipeline counters (feature 004, contracts/execution-plan.md §C8). Each field is a
/// plain <see cref="long"/> incremented on the render thread with no locks; "not observed" costs nothing
/// beyond a null check at the call site. One instance is owned per <see cref="RenderNodeProcessor"/> and
/// seeded into every <see cref="RenderNodeContext"/> it pulls. Read via <see cref="Snapshot"/> in tests.
/// </summary>
public sealed class PipelineDiagnostics
{
    /// <summary>
    /// C8: each executed draw/dispatch of a pass. On the legacy pipeline this counts every effect-path
    /// materialization bake draw plus every custom effect pass (SKSL <c>ApplyToNewTarget</c> draw, GLSL
    /// pipeline dispatch, and each <see cref="ImmediateCanvas"/> composite session opened by a custom effect).
    /// </summary>
    public long GpuPasses;

    /// <summary>
    /// C8: each fresh GPU target creation (pool miss or non-pooled). On the legacy pipeline this counts the
    /// fresh <see cref="RenderTarget"/> each <see cref="Effects.FilterEffectActivator"/> bake allocates plus
    /// each successful <see cref="Effects.CustomFilterEffectContext.CreateTarget"/>. Non-effect surfaces
    /// (the root render target, per-operation rasterization) are deliberately not counted.
    /// </summary>
    public long TargetAllocations;

    /// <summary>C8: each render-target pool acquire. Stays 0 until the pool lands (rollout step 2).</summary>
    public long PoolAcquires;

    /// <summary>C8: each pool acquire that had to allocate (a miss). Stays 0 until the pool lands (rollout step 2).</summary>
    public long PoolMisses;

    /// <summary>
    /// C8: each bake of an accumulated chain into a target. On the legacy pipeline this is each
    /// <see cref="Effects.FilterEffectActivator.Flush"/> materialization — including the forced flush an
    /// adjacent custom item pays even with no pending Skia chain (research §0 cost model).
    /// </summary>
    public long FullFrameMaterializations;

    /// <summary>
    /// C8: each backend-transition sync pair (C4.2). Counted at the effect-path call sites, not inside
    /// <see cref="RenderTarget"/>, because <see cref="RenderTarget.PrepareForSampling"/> /
    /// <see cref="RenderTarget.BeginDraw"/> also fire for non-effect surfaces (root draw, snapshot readback).
    /// On the legacy pipeline each effect-path GPU buffer is drawn (BeginDraw) then sampled (PrepareForSampling)
    /// uncoordinated, so this increments once per effect-path draw session: each activator bake and each custom
    /// effect canvas open.
    /// </summary>
    public long FlushSyncs;

    /// <summary>C8: each graph compile. Stays 0 until the compiler lands (rollout step 3).</summary>
    public long PlanCompilations;

    /// <summary>C8: each <c>SKRuntimeEffect</c> / Vulkan pipeline construction. Stays 0 until the program cache lands (rollout step 4).</summary>
    public long ProgramCreations;

    /// <summary>Takes an immutable copy of the current counter values for test assertions.</summary>
    public PipelineDiagnosticsSnapshot Snapshot() => new(
        GpuPasses,
        TargetAllocations,
        PoolAcquires,
        PoolMisses,
        FullFrameMaterializations,
        FlushSyncs,
        PlanCompilations,
        ProgramCreations);

    /// <summary>Resets every counter to zero.</summary>
    public void Reset()
    {
        GpuPasses = 0;
        TargetAllocations = 0;
        PoolAcquires = 0;
        PoolMisses = 0;
        FullFrameMaterializations = 0;
        FlushSyncs = 0;
        PlanCompilations = 0;
        ProgramCreations = 0;
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
    long ProgramCreations);
