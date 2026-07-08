using System.Collections.Immutable;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>Which device backend a <see cref="CompiledPass"/> runs on (feature 004, data-model §3).</summary>
public enum PassBackend
{
    /// <summary>A Skia draw (runs on raster/SwiftShader too, no Vulkan required).</summary>
    Skia,

    /// <summary>A Vulkan compute dispatch.</summary>
    Vulkan,
}

/// <summary>
/// One scheduled pass of a <see cref="CompiledPlan"/> (feature 004, data-model §3). Carries the logical bounds
/// it maps and the backward bounds function the per-frame resource resolution walks to compute the device ROI,
/// plus the schedule-time <see cref="SyncBefore"/> flag (set only at backend transitions, C4.2) and the
/// <see cref="IsDynamicOutputs"/> flag (execution-time-resolved output count, exempt from the static peak-live
/// bound). Concrete device sizes and ROIs are <em>not</em> stored here — they are recomputed every frame.
/// </summary>
public abstract record CompiledPass
{
    /// <summary>The device backend this pass runs on.</summary>
    public abstract PassBackend Backend { get; }

    /// <summary>The logical input bounds this pass consumes (from the described graph).</summary>
    public Rect InputBounds { get; init; }

    /// <summary>The logical output bounds this pass produces.</summary>
    public Rect OutputBounds { get; init; }

    /// <summary>True when this pass cannot lay out until execution; the resolver falls back to full input bounds for the ROI.</summary>
    public bool IsRenderTimeResolved { get; init; }

    /// <summary>Backward map (required input region for a requested output region); identity for invariant passes.</summary>
    internal Func<Rect, Rect> BackwardBounds { get; init; } = static r => r;

    /// <summary>
    /// Forward map (output region an input region produces); the composition of the pass's node forward bounds.
    /// The executor applies it to a fan-out branch's own bounds so each branch is sized from its post-split rect
    /// rather than the graph-level <see cref="OutputBounds"/> computed before the split (review B1). Identity for
    /// invariant passes; returns <see cref="Rect.Invalid"/> for a render-time-resolved pass.
    /// </summary>
    internal Func<Rect, Rect> ForwardBounds { get; init; } = static r => r;

    /// <summary>Set when this pass reads a resource last written by the other backend (C4.2); the only place the executor syncs.</summary>
    public bool SyncBefore { get; init; }

    /// <summary>Set when the output count is resolved at execution time (contour-based part splitting); exempt from the static peak-live bound (C3.5).</summary>
    public bool IsDynamicOutputs { get; init; }
}

/// <summary>One stage of a <see cref="FusedShaderPass"/> (feature 004, data-model §3). Executed in order as one draw.</summary>
public abstract record FusedStage;

/// <summary>A runtime SKSL stage: a snippet or whole-source shader whose <c>src</c> child is the accumulated shader.</summary>
public sealed record RuntimeShaderStage(
    SkslSource Source,
    ImmutableArray<UniformBinding> Uniforms,
    ImmutableArray<SamplerBinding> Samplers,
    ImmutableArray<ChildBinding> Children) : FusedStage
{
    /// <summary>
    /// The tile mode for the implicit <c>src</c> child of a whole-source stage (feature 004, D7): a whole-source
    /// shader samples <c>src</c> at arbitrary coordinates, so out-of-bounds reads must reproduce the legacy custom
    /// effect's tiling (<see cref="SKShaderTileMode.Clamp"/>/<see cref="SKShaderTileMode.Decal"/>). Irrelevant to a
    /// coordinate-invariant snippet run (it only samples the current pixel); left at the <c>Decal</c> default there.
    /// </summary>
    public SKShaderTileMode SrcTileMode { get; init; } = SKShaderTileMode.Decal;
}

/// <summary>A color-filter stage: wraps the accumulated shader with <c>SKShader.WithColorFilter</c>.</summary>
public sealed record ColorFilterStage(Func<SKColorFilter?> Factory) : FusedStage;

/// <summary>
/// A fused group of coordinate-invariant nodes compiled to one draw (feature 004, C1/D2). The ordered
/// <see cref="Stages"/> alternate runtime-shader and color-filter stages; the executor composes them from the
/// input image shader (child-shader nesting + <c>WithColorFilter</c> wraps) and draws once, preserving the
/// premultiplied linear-light representation between stages. Adjacent runtime snippets are merged into one
/// generated program by <see cref="SkslSnippetMerger"/> (an optimization on top).
/// </summary>
public sealed record FusedShaderPass(ImmutableArray<FusedStage> Stages) : CompiledPass
{
    /// <summary>
    /// True when every stage is coordinate-invariant, so the pass's output bounds equal its input's and the
    /// executor may size/place it from the operation's own bounds. A pass wrapping a single non-invariant
    /// whole-source stage (e.g. a channel-shift shader whose output rect differs from its input) sets this
    /// <see langword="false"/> and is sized/placed by its resolved ROI / declared forward bounds instead.
    /// </summary>
    public bool CoordinateInvariant { get; init; } = true;

    /// <inheritdoc/>
    public override PassBackend Backend => PassBackend.Skia;
}

/// <summary>
/// A group of adjacent Skia image-filter nodes composed into one filtered draw (feature 004, C2). Each factory
/// composes over the previous filter, reproducing the legacy builder's image-filter accumulation as a plan pass.
/// </summary>
public sealed record SkiaFilterPass(ImmutableArray<Func<SKImageFilter?, SKImageFilter?>> Filters) : CompiledPass
{
    /// <inheritdoc/>
    public override PassBackend Backend => PassBackend.Skia;
}

/// <summary>
/// An imperative geometry pass (feature 004, data-model §3, T040): the executor bakes the node's single input,
/// opens a bracketed canvas over a pooled output target, and invokes <see cref="Render"/> with a
/// <see cref="GeometrySession"/>. One draw = one output; never fused.
/// </summary>
public sealed record GeometryPass(Action<GeometrySession> Render) : CompiledPass
{
    /// <inheritdoc/>
    public override PassBackend Backend => PassBackend.Skia;
}

/// <summary>
/// A Vulkan compute pass (feature 004, data-model §3, T040): the executor materializes the input texture, hands
/// the node pooled ping-pong/depth textures, and runs <see cref="Dispatch"/>. <see cref="PassCount"/> dispatches
/// = <see cref="PassCount"/> <c>GpuPasses</c> (C8). On a context without Vulkan the declared <see cref="Fallback"/>
/// applies.
/// </summary>
public sealed record ComputePass(
    Action<IComputeContext> Dispatch,
    int PassCount,
    bool RequiresDepth,
    ComputeFallback Fallback,
    Action<GeometrySession>? CpuCallback) : CompiledPass
{
    /// <inheritdoc/>
    public override PassBackend Backend => PassBackend.Vulkan;
}

/// <summary>
/// A fan-out split pass (feature 004, data-model §3, T039): one input becomes N branch outputs the executor
/// allocates from the pool. <see cref="BranchCount"/> is the structural static count (0 for a dynamic split,
/// which sets <see cref="CompiledPass.IsDynamicOutputs"/>). Fusion never crosses a split.
/// </summary>
public sealed record SplitPass(Action<ISplitEmitter> Render, int BranchCount) : CompiledPass
{
    /// <inheritdoc/>
    public override PassBackend Backend => PassBackend.Skia;
}

/// <summary>
/// A fan-in composite pass (feature 004, data-model §3, T039): the current branch set is composited back into one
/// output under <see cref="BlendMode"/> with per-branch <see cref="InputOffsets"/>. Fusion never crosses a composite.
/// </summary>
/// <remarks>
/// <see cref="InputColorFilters"/> holds the color-filter factories of a coordinate-invariant color-filter run that
/// immediately preceded this composite and was folded into it by the compiler (contracts/execution-plan.md C9):
/// the composite draws each branch once, so applying the composed <c>SKColorFilter</c> to each branch's draw is
/// identical to first baking each branch through the filter and then compositing — eliminating the intermediate
/// pass and its per-branch targets. Empty when nothing folded; the factories are per-frame parameters (re-extracted
/// on a plan-cache hit), so an animated fold amount rebinds without a recompile.
/// </remarks>
public sealed record CompositePass(BlendMode BlendMode, ImmutableArray<Point> InputOffsets) : CompiledPass
{
    /// <summary>The folded color-filter factories applied to each branch draw, in node order (C9); empty when none folded.</summary>
    public ImmutableArray<Func<SKColorFilter?>> InputColorFilters { get; init; } = [];

    /// <inheritdoc/>
    public override PassBackend Backend => PassBackend.Skia;
}

/// <summary>
/// A per-branch nested graph pass (feature 004, research D8): for each current operation the executor invokes
/// <see cref="DescribeBranch"/> with the branch index, compiles the described child graph, and executes it
/// recursively against that single operation through the same pipeline (plans, pool, counters). Its outputs and
/// buffers are execution-time-resolved (<see cref="CompiledPass.IsDynamicOutputs"/>), exempt from the static
/// peak-live bound.
/// </summary>
public sealed record NestedGraphPass(Action<EffectGraphBuilder, int> DescribeBranch) : CompiledPass
{
    /// <inheritdoc/>
    public override PassBackend Backend => PassBackend.Skia;
}

/// <summary>
/// The structural shape of one intermediate buffer (feature 004, data-model §3, C3.1). The concrete
/// <c>DevicePixelSize</c> is resolved every frame; only the format and lifetime interval are structural.
/// Peak-live count = maximum overlap of <c>[FirstUse, LastUse]</c> intervals (the FR-007 bound).
/// </summary>
public sealed record IntermediateDecl(int Id, TextureFormat Format, int FirstUse, int LastUse);

/// <summary>
/// The structural half of a plan's resource plan (feature 004, data-model §3): the intermediate declarations and
/// their peak-live count. Concrete sizes/ROIs are computed per frame by the resource resolution pass.
/// </summary>
public sealed record ResourcePlan(ImmutableArray<IntermediateDecl> Intermediates)
{
    /// <summary>Maximum number of declared intermediates live at once (max overlap of lifetime intervals); the FR-007 bound.</summary>
    public int PeakLiveCount { get; } = ComputePeakLive(Intermediates);

    private static int ComputePeakLive(ImmutableArray<IntermediateDecl> decls)
    {
        int peak = 0;
        foreach (IntermediateDecl outer in decls)
        {
            int live = 0;
            foreach (IntermediateDecl inner in decls)
            {
                if (inner.FirstUse <= outer.FirstUse && outer.FirstUse <= inner.LastUse)
                    live++;
            }

            if (live > peak)
                peak = live;
        }

        return peak;
    }
}

/// <summary>
/// A compiled effect graph (feature 004, data-model §3): the structural key, the topologically ordered pass
/// schedule, and the resource plan's structural shape. Bounds, ROIs, buffer sizes and the resolved working scale
/// are per-frame resolution inputs, not part of this plan's identity (C5).
/// </summary>
public sealed record CompiledPlan(
    StructuralKey Key,
    ImmutableArray<CompiledPass> Passes,
    ResourcePlan Resources);
