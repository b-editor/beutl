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
    ImmutableArray<ChildBinding> Children) : FusedStage;

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
    /// <inheritdoc/>
    public override PassBackend Backend => PassBackend.Skia;
}

/// <summary>
/// A group of adjacent Skia image-filter nodes composed into one filtered draw (feature 004, C2). Each factory
/// composes over the previous filter, reproducing today's <c>SKImageFilterBuilder</c> accumulation as a plan pass.
/// </summary>
public sealed record SkiaFilterPass(ImmutableArray<Func<SKImageFilter?, SKImageFilter?>> Filters) : CompiledPass
{
    /// <inheritdoc/>
    public override PassBackend Backend => PassBackend.Skia;
}

/// <summary>
/// A fan-in composite pass (feature 004, data-model §3). Defined for plan-shape completeness; the compiler does
/// not emit one until the split/composite primitives migrate in a later step.
/// </summary>
public sealed record CompositePass(BlendMode BlendMode, ImmutableArray<Point> InputOffsets) : CompiledPass
{
    /// <inheritdoc/>
    public override PassBackend Backend => PassBackend.Skia;
}

/// <summary>
/// A transition-only pass (feature 004, rollout step 3) wrapping an unmigrated effect's recorded legacy item
/// list. The executor runs it through the retained (internal-only) activator machinery, deferring <b>all</b>
/// counter attribution to that machinery so bridged content's counters are byte-identical to today's. Deleted
/// with the bridge in step 6.
/// </summary>
internal sealed record OpaqueLegacyPass(FilterEffectContext Context) : CompiledPass
{
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
