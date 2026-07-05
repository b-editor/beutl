using System.Collections.Immutable;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Concrete device size and ROI of one pass for the current frame (feature 004, data-model §3, C3). Recomputed
/// every frame by <see cref="EffectGraphCompiler.ResolveResources"/>; never part of the plan's cache identity.
/// </summary>
internal sealed record PassResolution(Rect OutputRoi, int Width, int Height, float WorkingScale, bool SkipEmpty);

/// <summary>The per-frame resource resolution result: one <see cref="PassResolution"/> per plan pass, in schedule order.</summary>
internal sealed record FrameResources(ImmutableArray<PassResolution> Passes);

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
                    IsRenderTimeResolved = shader.Bounds.IsRenderTimeResolved,
                });
                i++;
            }
            else if (node.Descriptor is OpaqueLegacyNodeDescriptor opaque)
            {
                passes.Add(new OpaqueLegacyPass(opaque.Context)
                {
                    InputBounds = node.InputBounds,
                    OutputBounds = node.OutputBounds,
                    IsRenderTimeResolved = true,
                });
                i++;
            }
            else
            {
                throw new NotSupportedException(
                    $"Effect node descriptor '{node.Descriptor.GetType().Name}' is not supported by the step-3a compiler.");
            }
        }

        return passes.ToImmutable();
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

        int i = start;
        while (i < nodes.Count && IsFusable(nodes[i].Descriptor) && stages.Count < MaxFusionStages)
        {
            stages.Add(ToStage(nodes[i].Descriptor));
            outputBounds = nodes[i].OutputBounds;
            i++;
        }

        passes.Add(new FusedShaderPass(stages.ToImmutable())
        {
            InputBounds = inputBounds,
            OutputBounds = outputBounds,
            BackwardBounds = static r => r,
        });
        return i;
    }

    private static int EmitSkiaPass(IReadOnlyList<EffectNode> nodes, int start, ImmutableArray<CompiledPass>.Builder passes)
    {
        var filters = ImmutableArray.CreateBuilder<Func<SkiaSharp.SKImageFilter?, SkiaSharp.SKImageFilter?>>();
        Rect inputBounds = nodes[start].InputBounds;
        Rect outputBounds = inputBounds;
        bool renderTime = false;

        int end = start;
        while (end < nodes.Count && nodes[end].Descriptor is SkiaFilterNodeDescriptor filter)
        {
            filters.Add(filter.Factory);
            outputBounds = nodes[end].OutputBounds;
            renderTime |= filter.Bounds.IsRenderTimeResolved;
            end++;
        }

        Func<Rect, Rect> backward = static r => r;
        for (int k = end - 1; k >= start; k--)
        {
            Func<Rect, Rect> fk = ((SkiaFilterNodeDescriptor)nodes[k].Descriptor).Bounds.GetRequiredInputBounds;
            Func<Rect, Rect> inner = backward;
            backward = roi => fk(inner(roi));
        }

        passes.Add(new SkiaFilterPass(filters.ToImmutable())
        {
            InputBounds = inputBounds,
            OutputBounds = outputBounds,
            BackwardBounds = backward,
            IsRenderTimeResolved = renderTime,
        });
        return end;
    }

    private static FusedStage ToStage(EffectNodeDescriptor descriptor) => descriptor switch
    {
        ShaderNodeDescriptor shader => new RuntimeShaderStage(
            shader.Source, shader.Uniforms, shader.Samplers, shader.Children),
        ColorFilterNodeDescriptor colorFilter => new ColorFilterStage(colorFilter.Factory),
        _ => throw new NotSupportedException($"'{descriptor.GetType().Name}' is not a fused stage."),
    };

    private static ResourcePlan BuildResourcePlan(ImmutableArray<CompiledPass> passes)
    {
        var decls = ImmutableArray.CreateBuilder<IntermediateDecl>(passes.Length);
        for (int idx = 0; idx < passes.Length; idx++)
        {
            // A pass writes one intermediate; the next pass reads it (its own output is the frame result at the tail).
            int lastUse = idx < passes.Length - 1 ? idx + 1 : idx;
            decls.Add(new IntermediateDecl(idx, TextureFormat.RGBA16Float, idx, lastUse));
        }

        return new ResourcePlan(decls.ToImmutable());
    }

    /// <summary>
    /// Recomputes per-frame device sizes and ROIs (feature 004, C3). Walks the passes backward from the requested
    /// output region applying each pass's backward bounds (full-bounds fallback for render-time passes), then
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
            return new FrameResources([]);

        var outputRoi = new Rect[n];
        outputRoi[n - 1] = ResolveLastRoi(passes[n - 1], requestedBounds);
        for (int k = n - 2; k >= 0; k--)
        {
            CompiledPass next = passes[k + 1];
            Rect required = next.IsRenderTimeResolved || outputRoi[k + 1].IsInvalid
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
            (int bw, int bh) = CustomFilterEffectContext.DeviceBufferSize(sizeBounds, clamped);
            bool skip = bw <= 0 || bh <= 0;
            resolutions.Add(new PassResolution(outputRoi[k], bw, bh, clamped, skip));
            w = MathF.Min(w, clamped);
        }

        return new FrameResources(resolutions.MoveToImmutable());
    }

    private static Rect ResolveLastRoi(CompiledPass pass, Rect requestedBounds)
    {
        if (pass.IsRenderTimeResolved || pass.OutputBounds.IsInvalid)
            return FullBounds(pass);

        return requestedBounds.IsInvalid ? pass.OutputBounds : pass.OutputBounds.Intersect(requestedBounds);
    }

    private static Rect ClampToOutput(CompiledPass pass, Rect required)
    {
        if (pass.IsRenderTimeResolved || pass.OutputBounds.IsInvalid)
            return FullBounds(pass);

        return required.IsInvalid ? pass.OutputBounds : pass.OutputBounds.Intersect(required);
    }

    private static Rect FullBounds(CompiledPass pass)
        => pass.OutputBounds.IsInvalid ? pass.InputBounds : pass.OutputBounds;
}
