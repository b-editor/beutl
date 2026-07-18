using System.Collections.Immutable;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Concrete device size and ROI of one pass for the current frame (feature 004, data-model §3, C3). Recomputed
/// every frame by <see cref="EffectGraphCompiler.ResolveResources"/>; never part of the plan's cache identity.
/// </summary>
internal sealed record PassResolution(Rect OutputRoi, int Width, int Height, float WorkingScale, bool SkipEmpty);

/// <summary>
/// The per-frame resource resolution result: one <see cref="PassResolution"/> per plan pass, in schedule order.
/// <see cref="MaxDimension"/> carries the per-axis cap <see cref="EffectGraphCompiler.ResolveResources"/> clamped
/// against, so the executor's render-time re-clamps (shift/grow, fan-out, composite fold) honor the same cap.
/// </summary>
internal sealed record FrameResources(
    ImmutableArray<PassResolution> Passes, int MaxDimension = RenderNodeContext.MaxBufferDimension);

/// <summary>
/// Compiles an <see cref="EffectGraph"/> into a <see cref="CompiledPlan"/> and re-resolves per-frame resources
/// (feature 004, T021, contracts/execution-plan.md C1–C3). Compilation groups a maximal run of adjacent
/// coordinate-invariant nodes into one <see cref="FusedShaderPass"/> (splitting at the 16-stage budget, C1.4),
/// adjacent Skia-filter nodes into one <see cref="SkiaFilterPass"/> (C2), and each opaque/whole-source node into
/// its own pass. It builds the <see cref="ResourcePlan"/>'s structural shape (formats + lifetime intervals) but
/// computes no device sizes — those come from <see cref="ResolveResources"/>, which runs every frame (cache hit or
/// miss) as pure <see cref="Rect"/> math over the freshly described bounds, applying the backward ROI with the
/// render-time fallback (C3) and the monotonic working-scale carry with the 003 per-axis clamp (C3.2).
/// </summary>
internal static class EffectGraphCompiler
{
    /// <summary>Maximum shader stages composed into one fused pass before it splits into a consecutive pass (C1.4, FR-005).</summary>
    public const int MaxFusionStages = 16;

    /// <summary>
    /// Conservative fragment-uniform floor used for fused runtime programs, expressed as four-component vectors.
    /// A single authoring node may consume more (it cannot be split internally), but fusion never combines it with
    /// another node and never makes an otherwise-valid chain exceed the backend floor (FR-005).
    /// </summary>
    internal const int MaxFusionUniformVectors = 224;

    public static CompiledPlan Compile(EffectGraph graph, PipelineDiagnostics? diagnostics)
    {
        if (diagnostics != null)
            diagnostics.PlanCompilations++;

        ImmutableArray<CompiledPass> compiled = BuildPasses(graph);
        return new CompiledPlan(StructuralKey.Compute(graph), compiled, BuildResourcePlan(compiled));
    }

    /// <summary>
    /// Groups the graph's nodes into the ordered pass schedule (fusion runs, Skia-filter runs, opaque/whole-source
    /// singletons), carrying each pass's per-frame bounds. Shared by <see cref="Compile"/> (cache miss) and
    /// <see cref="ParameterBlock.Extract"/> (cache hit): it performs no compilation accounting — neither the
    /// <see cref="PipelineDiagnostics.PlanCompilations"/> increment nor program creation happens here — so a hit
    /// rebuilds the schedule with the frame's fresh parameters/bounds without a recompile.
    /// </summary>
    internal static ImmutableArray<CompiledPass> BuildPasses(EffectGraph graph)
    {
        IReadOnlyList<EffectNode> nodes = graph.Nodes;
        var passes = ImmutableArray.CreateBuilder<CompiledPass>();

        int i = 0;
        while (i < nodes.Count)
        {
            EffectNode node = nodes[i];
            if (IsFusable(node.Descriptor))
            {
                i = EmitFusedPass(nodes, i, passes);
            }
            else if (node.Descriptor is SkiaFilterNodeDescriptor)
            {
                i = EmitSkiaPass(nodes, i, passes);
            }
            else if (node.Descriptor is ShaderNodeDescriptor shader)
            {
                passes.Add(new FusedShaderPass([ToStage(shader)])
                {
                    InputBounds = node.InputBounds,
                    OutputBounds = node.OutputBounds,
                    BackwardBounds = shader.Bounds.GetRequiredInputBounds,
                    ForwardBounds = shader.Bounds.TransformBounds,
                    RequiresFullInput = shader.Bounds.RequiresFullInput,
                    CoordinateInvariant = shader.IsCoordinateInvariant,
                    ProvenanceMinChild = node.ChildIndex,
                    ProvenanceMaxChild = node.ChildIndex,
                });
                i++;
            }
            else if (node.Descriptor is GeometryNodeDescriptor geometry)
            {
                passes.Add(new GeometryPass(geometry.Render, geometry.RequiresReadback)
                {
                    InputBounds = node.InputBounds,
                    OutputBounds = node.OutputBounds,
                    BackwardBounds = geometry.Bounds.GetRequiredInputBounds,
                    ForwardBounds = geometry.Bounds.TransformBounds,
                    RequiresFullInput = geometry.Bounds.RequiresFullInput,
                    BoundsIdentities = [geometry.Bounds.StructuralIdentity],
                    ProvenanceMinChild = node.ChildIndex,
                    ProvenanceMaxChild = node.ChildIndex,
                });
                i++;
            }
            else if (node.Descriptor is ComputeNodeDescriptor compute)
            {
                passes.Add(new ComputePass(
                    compute.Dispatch, compute.PassCount, compute.ColorScratchCount,
                    compute.Fallback, compute.DispatchFailureBehavior)
                {
                    InputBounds = node.InputBounds,
                    OutputBounds = node.OutputBounds,
                    BackwardBounds = compute.Bounds.GetRequiredInputBounds,
                    ForwardBounds = compute.Bounds.TransformBounds,
                    RequiresFullInput = compute.Bounds.RequiresFullInput,
                    BoundsIdentities = [compute.Bounds.StructuralIdentity],
                    ProvenanceMinChild = node.ChildIndex,
                    ProvenanceMaxChild = node.ChildIndex,
                });
                i++;
            }
            else if (node.Descriptor is SplitNodeDescriptor split)
            {
                passes.Add(new SplitPass(split.Render, split.BranchCount, split.RequiresReadback)
                {
                    InputBounds = node.InputBounds,
                    OutputBounds = node.OutputBounds,
                    RequiresFullInput = true,
                    IsDynamicOutputs = split.IsDynamicOutputs,
                    ProvenanceMinChild = node.ChildIndex,
                    ProvenanceMaxChild = node.ChildIndex,
                });
                i++;
            }
            else if (node.Descriptor is CompositeNodeDescriptor composite)
            {
                passes.Add(new CompositePass(composite.BlendMode, composite.InputOffsets)
                {
                    InputBounds = node.InputBounds,
                    OutputBounds = node.OutputBounds,
                    RequiresFullInput = true,
                    ProvenanceMinChild = node.ChildIndex,
                    ProvenanceMaxChild = node.ChildIndex,
                });
                i++;
            }
            else if (node.Descriptor is NestedGraphNodeDescriptor nested)
            {
                passes.Add(new NestedGraphPass(
                    nested.DescribeBranch,
                    nested.BranchesCompleted,
                    node.NestedPlanCache ?? throw new InvalidOperationException("A nested graph is missing its plan cache scope."))
                {
                    InputBounds = node.InputBounds,
                    OutputBounds = node.OutputBounds,
                    RequiresFullInput = true,
                    IsDynamicOutputs = true,
                    ProvenanceMinChild = node.ChildIndex,
                    ProvenanceMaxChild = node.ChildIndex,
                });
                i++;
            }
            else if (node.Descriptor is CustomRenderNodeDescriptor custom)
            {
                passes.Add(new CustomRenderNodePass(
                    custom.Resource,
                    custom.Factory,
                    node.CustomNodeCache
                    ?? throw new InvalidOperationException("A custom render node is missing its persistent cache scope."))
                {
                    InputBounds = node.InputBounds,
                    OutputBounds = node.OutputBounds,
                    RequiresFullInput = true,
                    IsDynamicOutputs = true,
                    ProvenanceMinChild = node.ChildIndex,
                    ProvenanceMaxChild = node.ChildIndex,
                });
                i++;
            }
            else
            {
                throw new NotSupportedException(
                    $"Effect node descriptor '{node.Descriptor.GetType().Name}' is not supported by the compiler.");
            }
        }

        return ApplySyncBefore(FoldColorFiltersIntoComposites(passes.ToImmutable()));
    }

    // Folds a coordinate-invariant color-filter-only FusedShaderPass that immediately precedes a CompositePass into
    // that composite's per-branch draw (C9). The fast path eliminates the fused pass and its per-branch intermediate
    // targets when branch and composite densities match. The original pass is retained as a runtime fallback because
    // applying a paint filter after density-changing texture interpolation is not equivalent to filtering first. Only
    // a pure color-filter run folds: a run containing a runtime SKSL stage is not an SKColorFilter and stays its own
    // pass. The fold is restricted to a SrcOver composite: a non-SrcOver composite needs a full-canvas SaveLayer per
    // branch (C9.5), and a transparent-affecting color filter riding that full-canvas layer would filter transparent
    // pixels OUTSIDE the branch bounds, diverging from the unfused plan whose intermediate is branch-bounded. Shared
    // by the compile and plan-cache-hit paths, so the folded shape is identical on both (the structural key promises it).
    private static ImmutableArray<CompiledPass> FoldColorFiltersIntoComposites(ImmutableArray<CompiledPass> passes)
    {
        int n = passes.Length;
        if (n < 2)
            return passes;

        ImmutableArray<CompiledPass>.Builder? folded = null;
        for (int i = 0; i < n; i++)
        {
            if (i + 1 < n
                && passes[i] is FusedShaderPass { Stages: var stages } fused
                && IsColorFilterOnly(stages)
                && passes[i + 1] is CompositePass { BlendMode: BlendMode.SrcOver } composite)
            {
                folded ??= CopyPrefix(passes, i);
                folded.Add(composite with
                {
                    InputColorFilters = ColorFilterFactories(stages),
                    InputColorFilterFallback = fused,
                });
                i++; // consumed both the fused pass and the composite
                continue;
            }

            folded?.Add(passes[i]);
        }

        return folded?.ToImmutable() ?? passes;
    }

    private static bool IsColorFilterOnly(ImmutableArray<FusedStage> stages)
    {
        foreach (FusedStage stage in stages)
        {
            if (stage is not ColorFilterStage)
                return false;
        }

        return stages.Length > 0;
    }

    private static ImmutableArray<Func<SkiaSharp.SKColorFilter?>> ColorFilterFactories(ImmutableArray<FusedStage> stages)
    {
        var factories = ImmutableArray.CreateBuilder<Func<SkiaSharp.SKColorFilter?>>(stages.Length);
        foreach (FusedStage stage in stages)
            factories.Add(((ColorFilterStage)stage).Factory);

        return factories.MoveToImmutable();
    }

    private static ImmutableArray<CompiledPass>.Builder CopyPrefix(ImmutableArray<CompiledPass> passes, int count)
    {
        var builder = ImmutableArray.CreateBuilder<CompiledPass>(passes.Length);
        for (int j = 0; j < count; j++)
            builder.Add(passes[j]);

        return builder;
    }

    // Sets SyncBefore when the previous pass's output must return to the backend that starts the next pass. A compute
    // pass starts by materializing its input through Skia, then performs its own Skia->Vulkan sampling transition in
    // ExecuteCompute. Consequently Compute->Compute has SyncBefore=true for Vulkan->Skia plus the internal transition;
    // treating both passes as superficially Vulkan would undercount the synchronization actually paid.
    private static ImmutableArray<CompiledPass> ApplySyncBefore(ImmutableArray<CompiledPass> passes)
    {
        if (passes.IsDefaultOrEmpty)
            return passes;

        // The common case (a single-backend, all-Skia plan) needs no flag change; skip the rebuild entirely so a
        // plan-cache-hit rebind of such a plan re-derives zero syncs without allocating a builder + array.
        ImmutableArray<CompiledPass>.Builder? builder = null;
        PassBackend previous = PassBackend.Skia;
        for (int k = 0; k < passes.Length; k++)
        {
            PassBackend entryBackend = passes[k] is ComputePass ? PassBackend.Skia : passes[k].Backend;
            bool sync = entryBackend != previous;
            if (sync != passes[k].SyncBefore)
            {
                builder ??= passes.ToBuilder();
                builder[k] = passes[k] with { SyncBefore = sync };
            }

            previous = passes[k].Backend;
        }

        return builder?.ToImmutable() ?? passes;
    }

    private static bool IsFusable(EffectNodeDescriptor descriptor) => descriptor switch
    {
        ShaderNodeDescriptor { IsCoordinateInvariant: true, Source.Kind: SkslSourceKind.Snippet } => true,
        ColorFilterNodeDescriptor => true,
        _ => false,
    };

    private static int EmitFusedPass(IReadOnlyList<EffectNode> nodes, int start, ImmutableArray<CompiledPass>.Builder passes)
    {
        var stages = ImmutableArray.CreateBuilder<FusedStage>();
        Rect inputBounds = nodes[start].InputBounds;
        Rect outputBounds = inputBounds;
        int minChild = nodes[start].ChildIndex;
        int maxChild = nodes[start].ChildIndex;
        int uniformVectors = 0;

        int i = start;
        while (i < nodes.Count && IsFusable(nodes[i].Descriptor) && stages.Count < MaxFusionStages)
        {
            int nextUniformVectors = GetUniformVectorCount(nodes[i].Descriptor);
            if (stages.Count > 0 && uniformVectors + nextUniformVectors > MaxFusionUniformVectors)
                break;

            stages.Add(ToStage(nodes[i].Descriptor));
            uniformVectors += nextUniformVectors;
            outputBounds = nodes[i].OutputBounds;
            minChild = Math.Min(minChild, nodes[i].ChildIndex);
            maxChild = Math.Max(maxChild, nodes[i].ChildIndex);
            i++;
        }

        passes.Add(new FusedShaderPass(stages.ToImmutable())
        {
            InputBounds = inputBounds,
            OutputBounds = outputBounds,
            BackwardBounds = static r => r,
            ProvenanceMinChild = minChild,
            ProvenanceMaxChild = maxChild,
        });
        return i;
    }

    private static int GetUniformVectorCount(EffectNodeDescriptor descriptor)
    {
        if (descriptor is not ShaderNodeDescriptor shader)
            return 0;

        return Math.Min(shader.Source.UniformVectorCount, MaxFusionUniformVectors + 1);
    }

    private static int EmitSkiaPass(IReadOnlyList<EffectNode> nodes, int start, ImmutableArray<CompiledPass>.Builder passes)
    {
        var filters = ImmutableArray.CreateBuilder<Func<SkiaSharp.SKImageFilter?, SkiaSharp.SKImageFilter?>>();
        var boundsIdentities = ImmutableArray.CreateBuilder<BoundsStructuralIdentity>();
        Rect inputBounds = nodes[start].InputBounds;
        Rect outputBounds = inputBounds;
        bool requiresFullInput = false;
        int minChild = nodes[start].ChildIndex;
        int maxChild = nodes[start].ChildIndex;

        int end = start;
        while (end < nodes.Count && nodes[end].Descriptor is SkiaFilterNodeDescriptor filter)
        {
            filters.Add(filter.Factory);
            boundsIdentities.Add(filter.Bounds.StructuralIdentity);
            outputBounds = nodes[end].OutputBounds;
            requiresFullInput |= filter.Bounds.RequiresFullInput;
            minChild = Math.Min(minChild, nodes[end].ChildIndex);
            maxChild = Math.Max(maxChild, nodes[end].ChildIndex);
            end++;
        }

        Func<Rect, Rect> backward = static r => r;
        for (int k = end - 1; k >= start; k--)
        {
            Func<Rect, Rect> fk = ((SkiaFilterNodeDescriptor)nodes[k].Descriptor).Bounds.GetRequiredInputBounds;
            Func<Rect, Rect> inner = backward;
            backward = roi => fk(inner(roi));
        }

        Func<Rect, Rect> forward = static r => r;
        for (int k = start; k < end; k++)
        {
            Func<Rect, Rect> fk = ((SkiaFilterNodeDescriptor)nodes[k].Descriptor).Bounds.TransformBounds;
            Func<Rect, Rect> inner = forward;
            forward = r => fk(inner(r));
        }

        passes.Add(new SkiaFilterPass(filters.ToImmutable())
        {
            InputBounds = inputBounds,
            OutputBounds = outputBounds,
            BackwardBounds = backward,
            ForwardBounds = forward,
            RequiresFullInput = requiresFullInput,
            BoundsIdentities = boundsIdentities.ToImmutable(),
            ProvenanceMinChild = minChild,
            ProvenanceMaxChild = maxChild,
        });
        return end;
    }

    private static FusedStage ToStage(EffectNodeDescriptor descriptor) => descriptor switch
    {
        ShaderNodeDescriptor shader => new RuntimeShaderStage(
            shader.Source, shader.Uniforms, shader.Children)
        {
            SrcTileMode = shader.SrcTileMode,
            BoundsIdentity = shader.Bounds.StructuralIdentity,
        },
        ColorFilterNodeDescriptor colorFilter => new ColorFilterStage(colorFilter.Factory),
        _ => throw new NotSupportedException($"'{descriptor.GetType().Name}' is not a fused stage."),
    };

    // Declares every intermediate the executor concurrently holds, so PeakLiveCount bounds the runtime FR-007
    // assert: one output per current operation, read by the next pass ([idx, idx+1]; the tail's output is the
    // frame result), plus pass-scoped scratch ([idx, idx]) — the baked input for geometry/compute/split, and the
    // C3.3 color scratch set for compute. A dynamic-output pass (dynamic split, nested graph)
    // has no static decls (C3.5); the executor skips the peak assert for such plans.
    private static ResourcePlan BuildResourcePlan(ImmutableArray<CompiledPass> passes)
    {
        const int maxStaticDeclarations = 4096;
        var decls = ImmutableArray.CreateBuilder<IntermediateDecl>(passes.Length);
        int id = 0;
        int multiplicity = 1;

        bool Add(long count, int firstUse, int lastUse, TextureFormat format = TextureFormat.RGBA16Float)
        {
            if (count < 0 || count > maxStaticDeclarations - decls.Count)
                return false;

            for (int c = 0; c < (int)count; c++)
                decls.Add(new IntermediateDecl(id++, format, firstUse, lastUse));

            return true;
        }

        static ResourcePlan RuntimeBounded() => new([], IsStaticallyBounded: false);

        for (int idx = 0; idx < passes.Length; idx++)
        {
            int lastUse = idx < passes.Length - 1 ? idx + 1 : idx;
            switch (passes[idx])
            {
                case SplitPass { IsDynamicOutputs: false } split:
                    if (!Add(multiplicity, idx, idx)
                        || split.BranchCount > maxStaticDeclarations / multiplicity)
                    {
                        // A plugin may chain individually valid static splits into an enormous cumulative fan-out.
                        // Do not expand that product into millions of declarations (or overflow the multiplier):
                        // execution still enforces every split's exact branch count, while resource accounting uses
                        // the same runtime-resolved exemption as a dynamic-output plan.
                        return RuntimeBounded();
                    }

                    multiplicity *= split.BranchCount;
                    if (!Add(multiplicity, idx, lastUse))
                        return RuntimeBounded();
                    break;
                case SplitPass or NestedGraphPass or CustomRenderNodePass:
                    break;
                case CompositePass:
                    multiplicity = 1;
                    if (!Add(1, idx, lastUse))
                        return RuntimeBounded();
                    break;
                case GeometryPass:
                    if (!Add(multiplicity, idx, idx) || !Add(multiplicity, idx, lastUse))
                        return RuntimeBounded();
                    break;
                case ComputePass compute:
                    if (!Add(multiplicity, idx, idx)
                        || !Add((long)compute.ColorScratchCount * multiplicity, idx, idx)
                        || !Add(multiplicity, idx, lastUse))
                    {
                        return RuntimeBounded();
                    }
                    break;
                case FusedShaderPass { NeedsSourceHaloBake: true }:
                    // The source-halo bake buffer (§C3.1): acquired only when a downstream deflate narrows the
                    // output below the backward-claimed input rect; declared unconditionally as the upper bound.
                    if (!Add(multiplicity, idx, idx) || !Add(multiplicity, idx, lastUse))
                        return RuntimeBounded();
                    break;
                default:
                    if (!Add(multiplicity, idx, lastUse))
                        return RuntimeBounded();
                    break;
            }
        }

        return new ResourcePlan(decls.ToImmutable());
    }

    /// <summary>
    /// Recomputes per-frame device sizes and ROIs (feature 004, C3). Walks the passes backward from the requested
    /// output region applying each pass's backward bounds (complete input for full-frame passes), then
    /// forward in schedule order applying the monotonically non-increasing working-scale carry and the 003 per-axis
    /// clamp. Pure <see cref="Rect"/> math — never creates programs or targets.
    /// </summary>
    public static FrameResources ResolveResources(
        CompiledPlan plan, Rect requestedBounds, float workingScale,
        int maxDimension = RenderNodeContext.MaxBufferDimension)
    {
        ImmutableArray<CompiledPass> passes = plan.Passes;
        int n = passes.Length;
        if (n == 0)
            return new FrameResources([], maxDimension);

        var outputRoi = new Rect[n];
        outputRoi[n - 1] = ResolveLastRoi(passes[n - 1], requestedBounds);
        for (int k = n - 2; k >= 0; k--)
        {
            CompiledPass next = passes[k + 1];
            Rect required = next.RequiresFullInput || outputRoi[k + 1].IsInvalid
                ? FullBounds(passes[k])
                : next.BackwardBounds(outputRoi[k + 1]);
            outputRoi[k] = ClampToOutput(passes[k], required);
        }

        var resolutions = ImmutableArray.CreateBuilder<PassResolution>(n);
        float w = workingScale;
        for (int k = 0; k < n; k++)
        {
            Rect sizeBounds = outputRoi[k].IsInvalid ? FullBounds(passes[k]) : outputRoi[k];
            float clamped = RenderNodeContext.ClampWorkingScaleToBufferBudget(sizeBounds, w, maxDimension);
            (int bw, int bh) = RenderNodeContext.DeviceBufferSize(sizeBounds, clamped);
            bool skip = bw <= 0 || bh <= 0;
            resolutions.Add(new PassResolution(outputRoi[k], bw, bh, clamped, skip));
            w = MathF.Min(w, clamped);
        }

        return new FrameResources(resolutions.MoveToImmutable(), maxDimension);
    }

    private static Rect ResolveLastRoi(CompiledPass pass, Rect requestedBounds)
    {
        if (pass.RequiresFullInput || pass.OutputBounds.IsInvalid)
            return FullBounds(pass);

        return requestedBounds.IsInvalid ? pass.OutputBounds : pass.OutputBounds.Intersect(requestedBounds);
    }

    private static Rect ClampToOutput(CompiledPass pass, Rect required)
    {
        if (pass.RequiresFullInput || pass.OutputBounds.IsInvalid)
            return FullBounds(pass);

        return required.IsInvalid ? pass.OutputBounds : pass.OutputBounds.Intersect(required);
    }

    private static Rect FullBounds(CompiledPass pass)
        => pass.OutputBounds.IsInvalid ? pass.InputBounds : pass.OutputBounds;
}
