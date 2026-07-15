using System.Collections.Immutable;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

/// <summary>Which device backend a <see cref="CompiledPass"/> runs on (feature 004, data-model §3).</summary>
internal enum PassBackend
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
internal abstract record CompiledPass
{
    /// <summary>The device backend this pass runs on.</summary>
    public abstract PassBackend Backend { get; }

    /// <summary>The logical input bounds this pass consumes (from the described graph).</summary>
    public Rect InputBounds { get; init; }

    /// <summary>The logical output bounds this pass produces.</summary>
    public Rect OutputBounds { get; init; }

    /// <summary>True when this pass must receive the complete input instead of an ROI crop.</summary>
    public bool RequiresFullInput { get; init; }

    /// <summary>Exact structural identities of the bounds contracts represented by this pass.</summary>
    internal ImmutableArray<BoundsStructuralIdentity> BoundsIdentities { get; init; } = [];

    /// <summary>Backward map (required input region for a requested output region); identity for invariant passes.</summary>
    internal Func<Rect, Rect> BackwardBounds { get; init; } = static r => r;

    /// <summary>
    /// Forward map (output region an input region produces); the composition of the pass's node forward bounds.
    /// The executor applies it to a fan-out branch's own bounds so each branch is sized from its post-split rect
    /// rather than the graph-level <see cref="OutputBounds"/> computed before the split (review B1). Identity for
    /// invariant and full-frame passes.
    /// </summary>
    internal Func<Rect, Rect> ForwardBounds { get; init; } = static r => r;

    /// <summary>Set when this pass reads a resource last written by the other backend (C4.2).</summary>
    public bool SyncBefore { get; init; }

    /// <summary>Set when the output count is resolved at execution time (contour-based part splitting); exempt from the static peak-live bound (C3.5).</summary>
    public bool IsDynamicOutputs { get; init; }

    /// <summary>
    /// Lowest top-level group-child index whose descriptors this pass spans (feature 004, C10 provenance); <c>-1</c>
    /// when the pass carries no provenance (never in the leading linear prefix, e.g. a fold-synthesized composite).
    /// </summary>
    public int ProvenanceMinChild { get; init; } = -1;

    /// <summary>
    /// Highest top-level group-child index whose descriptors this pass spans (feature 004, C10 provenance). The
    /// pass-prefix output cache retains a pass only when its whole <c>[ProvenanceMinChild, ProvenanceMaxChild]</c>
    /// range lies within the stable leading run of children, so a fused pass spanning several children is reused
    /// only when every one of them is stable.
    /// </summary>
    public int ProvenanceMaxChild { get; init; } = -1;
}

/// <summary>One stage of a <see cref="FusedShaderPass"/> (feature 004, data-model §3). Executed in order as one draw.</summary>
internal abstract record FusedStage;

/// <summary>A runtime SKSL stage: a snippet or whole-source shader whose <c>src</c> child is the accumulated shader.</summary>
internal sealed record RuntimeShaderStage(
    SkslSource Source,
    ImmutableArray<UniformBinding> Uniforms,
    ImmutableArray<ChildBinding> Children) : FusedStage
{
    /// <summary>
    /// The tile mode for the implicit <c>src</c> child of a whole-source stage (feature 004, D7): a whole-source
    /// shader samples <c>src</c> at arbitrary coordinates, so out-of-bounds reads must reproduce the legacy custom
    /// effect's tiling (<see cref="SKShaderTileMode.Clamp"/>/<see cref="SKShaderTileMode.Decal"/>). Irrelevant to a
    /// coordinate-invariant snippet run (it only samples the current pixel); left at the <c>Decal</c> default there.
    /// </summary>
    public SKShaderTileMode SrcTileMode { get; init; } = SKShaderTileMode.Decal;

    /// <summary>Structural identity of the stage's forward/backward bounds contract.</summary>
    public BoundsStructuralIdentity BoundsIdentity { get; init; }
}

/// <summary>A color-filter stage: wraps the accumulated shader with <c>SKShader.WithColorFilter</c>.</summary>
internal sealed record ColorFilterStage(Func<SKColorFilter?> Factory) : FusedStage;

/// <summary>
/// A fused group of coordinate-invariant nodes compiled to one draw (feature 004, C1/D2). The ordered
/// <see cref="Stages"/> alternate runtime-shader and color-filter stages; the executor composes them from the
/// input image shader (child-shader nesting + <c>WithColorFilter</c> wraps) and draws once, preserving the
/// premultiplied linear-light representation between stages. Adjacent runtime snippets are merged into one
/// generated program by <see cref="SkslSnippetMerger"/> (an optimization on top).
/// </summary>
internal sealed record FusedShaderPass(ImmutableArray<FusedStage> Stages) : CompiledPass
{
    private FusedProgramLayout? _programLayout;

    /// <summary>
    /// True when every stage is coordinate-invariant, so the pass's output bounds equal its input's and the
    /// executor may size/place it from the operation's own bounds. A pass wrapping a single non-invariant
    /// whole-source stage (e.g. a channel-shift shader whose output rect differs from its input) sets this
    /// <see langword="false"/> and is sized/placed by its resolved ROI / declared forward bounds instead.
    /// </summary>
    public bool CoordinateInvariant { get; init; } = true;

    /// <summary>
    /// True when the executor may need a second target to bake source pixels outside the resolved output ROI.
    /// Resource planning and execution must use this single predicate so the FR-007 declaration cannot drift
    /// from the buffer the executor actually acquires.
    /// </summary>
    internal bool NeedsSourceHaloBake
        => !CoordinateInvariant
            && Stages is [RuntimeShaderStage { Source.Kind: SkslSourceKind.WholeSource }];

    /// <summary>
    /// Structural runtime-program metadata shared with every parameter-only rebind of this pass. It owns the merged
    /// source, signature, stage slice, and direct cache handle, so warm execution does not rebuild any of them.
    /// </summary>
    internal FusedProgramLayout ProgramLayout => _programLayout ??= FusedProgramLayout.Create(Stages);

    internal void ReuseProgramLayout(FusedShaderPass cached)
        => _programLayout = cached.ProgramLayout;

    /// <inheritdoc/>
    public override PassBackend Backend => PassBackend.Skia;
}

internal sealed record FusedProgramLayout(ImmutableArray<RuntimeProgram> RuntimePrograms)
{
    public static FusedProgramLayout Create(ImmutableArray<FusedStage> stages)
    {
        var programs = ImmutableArray.CreateBuilder<RuntimeProgram>();
        for (int i = 0; i < stages.Length;)
        {
            if (stages[i] is not RuntimeShaderStage)
            {
                i++;
                continue;
            }

            int start = i;
            while (i < stages.Length && stages[i] is RuntimeShaderStage)
                i++;
            programs.Add(RuntimeProgram.Create(stages, start, i - start));
        }

        return new FusedProgramLayout(programs.ToImmutable());
    }
}

internal sealed class RuntimeProgram
{
    internal RuntimeProgram(
        int startStage, int stageCount, bool isWholeSource, string signature,
        SkslSource[] sources, string sourceText)
    {
        StartStage = startStage;
        StageCount = stageCount;
        IsWholeSource = isWholeSource;
        Signature = signature;
        Sources = sources;
        SourceText = sourceText;
    }

    public int StartStage { get; }

    public int StageCount { get; }

    public bool IsWholeSource { get; }

    public string ChildName => IsWholeSource ? "src" : SkslSnippetMerger.SourceChildName;

    public string Signature { get; }

    public SkslSource[] Sources { get; }

    public string SourceText { get; }

    internal ProgramCache.Entry? CacheEntry { get; set; }

    public static RuntimeProgram Create(ImmutableArray<FusedStage> stages, int startStage, int stageCount)
    {
        var sources = new SkslSource[stageCount];
        for (int i = 0; i < stageCount; i++)
            sources[i] = ((RuntimeShaderStage)stages[startStage + i]).Source;

        bool wholeSource = stageCount == 1 && sources[0].Kind == SkslSourceKind.WholeSource;
        string signature;
        string sourceText;
        if (wholeSource)
        {
            signature = "w:" + sources[0].IdentityHash;
            sourceText = sources[0].Source;
        }
        else
        {
            var builder = new System.Text.StringBuilder("m:");
            for (int i = 0; i < sources.Length; i++)
            {
                if (i > 0)
                    builder.Append(',');
                builder.Append(sources[i].IdentityHash);
            }

            signature = builder.ToString();
            sourceText = SkslSnippetMerger.Merge(sources);
        }

        return new RuntimeProgram(startStage, stageCount, wholeSource, signature, sources, sourceText);
    }
}

/// <summary>
/// A group of adjacent Skia image-filter nodes composed into one filtered draw (feature 004, C2). Each factory
/// composes over the previous filter, reproducing the legacy builder's image-filter accumulation as a plan pass.
/// </summary>
internal sealed record SkiaFilterPass(ImmutableArray<Func<SKImageFilter?, SKImageFilter?>> Filters) : CompiledPass
{
    /// <inheritdoc/>
    public override PassBackend Backend => PassBackend.Skia;
}

/// <summary>
/// An imperative geometry pass (feature 004, data-model §3, T040): the executor bakes the node's single input,
/// opens a bracketed canvas over a pooled output target, and invokes <see cref="Render"/> with a
/// <see cref="GeometrySession"/>. One draw = one output; never fused.
/// </summary>
internal sealed record GeometryPass(Action<GeometrySession> Render, bool RequiresReadback) : CompiledPass
{
    /// <inheritdoc/>
    public override PassBackend Backend => PassBackend.Skia;
}

/// <summary>
/// A Vulkan compute pass (feature 004, data-model §3, T040): the executor materializes the input texture, hands
/// the node pooled ping-pong textures, and runs <see cref="Dispatch"/>. <see cref="PassCount"/> dispatches
/// = <see cref="PassCount"/> <c>GpuPasses</c> (C8). On a context without Vulkan the declared <see cref="Fallback"/>
/// applies.
/// </summary>
internal sealed record ComputePass(
    Action<IComputeContext> Dispatch,
    int PassCount,
    int ColorScratchCount,
    ComputeFallbackPolicy Fallback,
    ComputeDispatchFailureBehavior DispatchFailureBehavior) : CompiledPass
{
    /// <inheritdoc/>
    public override PassBackend Backend => PassBackend.Vulkan;
}

/// <summary>
/// A fan-out split pass (feature 004, data-model §3, T039): one input becomes N branch outputs the executor
/// allocates from the pool. <see cref="BranchCount"/> is the structural static count (0 for a dynamic split,
/// which sets <see cref="CompiledPass.IsDynamicOutputs"/>). Fusion never crosses a split.
/// </summary>
internal sealed record SplitPass(Action<ISplitEmitter> Render, int BranchCount, bool RequiresReadback) : CompiledPass
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
/// identical to first baking each branch through the filter and then compositing when the branch and composite
/// densities match. <see cref="InputColorFilterFallback"/> preserves the original pass so density-changing fan-in
/// can execute it at each branch's carried density before resampling. Empty when nothing folded; the factories are
/// per-frame parameters (re-extracted on a plan-cache hit), so an animated fold amount rebinds without a recompile.
/// </remarks>
internal sealed record CompositePass(BlendMode BlendMode, ImmutableArray<Point> InputOffsets) : CompiledPass
{
    /// <summary>The folded color-filter factories applied to each branch draw, in node order (C9); empty when none folded.</summary>
    public ImmutableArray<Func<SKColorFilter?>> InputColorFilters { get; init; } = [];

    /// <summary>
    /// The pre-fold color-filter pass used when a concrete branch density differs from the composite target density.
    /// </summary>
    public FusedShaderPass? InputColorFilterFallback { get; init; }

    /// <inheritdoc/>
    public override PassBackend Backend => PassBackend.Skia;
}

/// <summary>
/// A per-branch nested graph pass (feature 004, research D8): for each current operation the executor invokes
/// <see cref="DescribeBranch"/> with the branch index, rebinds or compiles it through that branch's persistent
/// hierarchical plan cache, and executes it recursively against that single operation through the same pipeline
/// (plans, pool, counters). Its outputs and
/// buffers are execution-time-resolved (<see cref="CompiledPass.IsDynamicOutputs"/>), exempt from the static
/// peak-live bound.
/// </summary>
internal sealed record NestedGraphPass(
    Action<EffectGraphBuilder, int> DescribeBranch,
    NestedGraphNodePlanCache PlanCache) : CompiledPass
{
    /// <inheritdoc/>
    public override PassBackend Backend => PassBackend.Skia;
}

/// <summary>
/// A custom-render-node pass (feature 004): the executor drives an effect's custom
/// <see cref="FilterEffectRenderNode"/> as one node of this plan — it hands the current ops to the child node's
/// <see cref="RenderNode.Process"/> (threading the shared diagnostics/pool and cache policy) and feeds the returned
/// ops onward. The child receives a full requested region because this opaque pass has no compiler-visible backward
/// bounds contract; forwarding the outer crop could clip pixels needed by a later expanding pass. The executor keeps
/// a persistent child node in the owning hierarchical runtime cache; retired nodes stay alive until every returned
/// operation is disposed, so deferred rendering may use node-owned state. Its
/// outputs are execution-time-resolved (<see cref="CompiledPass.IsDynamicOutputs"/>), so it terminates fusion and
/// the C10 prefix and is exempt from the static peak-live bound; the work it drives counts on the shared
/// <see cref="PipelineDiagnostics"/> like every other pass (C8). <see cref="NodeType"/> is part of the structural
/// identity so a swapped child type recompiles (plan-cache correctness).
/// </summary>
internal sealed record CustomRenderNodePass(
    Effects.FilterEffect.Resource Resource,
    FilterEffectRenderNodeFactory Factory,
    CustomRenderNodePlanCache NodeCache) : CompiledPass
{
    internal Type NodeType => Factory.NodeType;

    /// <inheritdoc/>
    public override PassBackend Backend => PassBackend.Skia;
}

/// <summary>
/// The structural shape of one intermediate buffer (feature 004, data-model §3, C3.1). The concrete
/// <c>DevicePixelSize</c> is resolved every frame; only the format and lifetime interval are structural.
/// Peak-live count = maximum overlap of <c>[FirstUse, LastUse]</c> intervals (the FR-007 bound).
/// </summary>
internal sealed record IntermediateDecl(int Id, TextureFormat Format, int FirstUse, int LastUse);

/// <summary>
/// The structural half of a plan's resource plan (feature 004, data-model §3): the intermediate declarations and
/// their peak-live count. Concrete sizes/ROIs are computed per frame by the resource resolution pass. A cumulative
/// fan-out too large to enumerate sets <see cref="IsStaticallyBounded"/> to <see langword="false"/> and uses runtime
/// accounting instead of materializing an unbounded declaration array.
/// </summary>
internal sealed record ResourcePlan(
    ImmutableArray<IntermediateDecl> Intermediates,
    bool IsStaticallyBounded = true)
{
    /// <summary>Maximum number of declared intermediates live at once (max overlap of lifetime intervals); the FR-007 bound.</summary>
    public int PeakLiveCount { get; } = ComputePeakLive(Intermediates);

    private static int ComputePeakLive(ImmutableArray<IntermediateDecl> decls)
    {
        if (decls.IsEmpty)
            return 0;

        int lastUse = 0;
        foreach (IntermediateDecl decl in decls)
            lastUse = Math.Max(lastUse, decl.LastUse);

        var deltas = new int[lastUse + 2];
        foreach (IntermediateDecl decl in decls)
        {
            deltas[decl.FirstUse]++;
            deltas[decl.LastUse + 1]--;
        }

        int peak = 0;
        int live = 0;
        foreach (int delta in deltas)
        {
            live += delta;
            peak = Math.Max(peak, live);
        }

        return peak;
    }
}

/// <summary>
/// A compiled effect graph (feature 004, data-model §3): the structural key, the topologically ordered pass
/// schedule, and the resource plan's structural shape. Bounds, ROIs, buffer sizes and the resolved working scale
/// are per-frame resolution inputs, not part of this plan's identity (C5).
/// </summary>
internal sealed record CompiledPlan(
    StructuralKey Key,
    ImmutableArray<CompiledPass> Passes,
    ResourcePlan Resources);
