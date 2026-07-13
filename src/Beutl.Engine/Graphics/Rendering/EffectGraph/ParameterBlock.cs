using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// The per-frame parameter payload of a re-described <see cref="EffectGraph"/> (feature 004, T033, data-model §4).
/// On a <see cref="PlanCache"/> hit the graph is re-described (cheap) but must not recompile; this block carries
/// the frame's mutable values — uniform bindings, color-filter/image-filter factories, sampler/child textures —
/// plus each pass's re-resolved bounds, and rebinds them onto the cached plan's structural identity via
/// <see cref="RebindOnto"/>.
/// </summary>
/// <remarks>
/// Structural-key equality (verified by the cache before a rebind) guarantees the fresh graph has the same node
/// sequence and grouping as the cached plan, so slot-mapping by pass/stage order is sound. The block is the same
/// <see cref="CompiledPass"/> schedule the compiler would emit — built by the shared
/// <see cref="EffectGraphCompiler.BuildPasses"/> grouping <b>without</b> the compile accounting — so it also
/// re-resolves bounds for animated spatial parameters (blur sigma, drop-shadow offset) and, critically, swaps the
/// cached passes' captured closures (uniform bindings, filter factories, session callbacks over last frame's
/// resource values) for this frame's, keeping animated parameters live on a cache hit.
/// </remarks>
internal sealed record ParameterBlock(ImmutableArray<CompiledPass> Passes)
{
    /// <summary>Re-describes the frame's passes (parameters + bounds) without compiling; used on a cache hit.</summary>
    public static ParameterBlock Extract(EffectGraph graph) => new(EffectGraphCompiler.BuildPasses(graph));

    /// <summary>
    /// Rebinds this frame's parameters onto <paramref name="cached"/>'s structural identity: the cache key and the
    /// structural resource plan are reused verbatim; the executable passes are this block's (fresh parameters and
    /// bounds). A release-build runtime guard enforces the slot-mapping invariant (identical pass/stage shape and
    /// shader-source identity) the structural key promised.
    /// </summary>
    public CompiledPlan RebindOnto(CompiledPlan cached)
    {
        if (!ShapesMatch(cached.Passes, Passes))
        {
            throw new InvalidOperationException(
                "The cached effect plan shape does not match the current structural key.");
        }

        return new CompiledPlan(cached.Key, Passes, cached.Resources);
    }

    private static bool ShapesMatch(ImmutableArray<CompiledPass> a, ImmutableArray<CompiledPass> b)
    {
        if (a.Length != b.Length)
            return false;

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].GetType() != b[i].GetType())
                return false;

            switch (a[i])
            {
                case FusedShaderPass fa when b[i] is FusedShaderPass fb:
                    if (!StagesMatch(fa.Stages, fb.Stages))
                        return false;
                    break;
                case SkiaFilterPass sa when b[i] is SkiaFilterPass sb:
                    if (sa.Filters.Length != sb.Filters.Length)
                        return false;
                    break;
                case CompositePass ca when b[i] is CompositePass cb:
                    if (ca.BlendMode != cb.BlendMode
                        || ca.InputOffsets.Length != cb.InputOffsets.Length
                        || ca.InputColorFilters.Length != cb.InputColorFilters.Length)
                        return false;
                    break;
                case ComputePass compa when b[i] is ComputePass compb:
                    if (compa.PassCount != compb.PassCount
                        || compa.ColorScratchCount != compb.ColorScratchCount
                        || compa.DepthScratchCount != compb.DepthScratchCount
                        || compa.Fallback != compb.Fallback
                        || compa.CpuFallbackRequiresReadback != compb.CpuFallbackRequiresReadback
                        || compa.DispatchFailureBehavior != compb.DispatchFailureBehavior)
                        return false;
                    break;
                case GeometryPass geoa when b[i] is GeometryPass geob:
                    if (geoa.RequiresReadback != geob.RequiresReadback)
                        return false;
                    break;
                case SplitPass splita when b[i] is SplitPass splitb:
                    if (splita.BranchCount != splitb.BranchCount
                        || splita.IsDynamicOutputs != splitb.IsDynamicOutputs
                        || splita.RequiresReadback != splitb.RequiresReadback)
                        return false;
                    break;
                case CustomRenderNodePass customa when b[i] is CustomRenderNodePass customb:
                    if (customa.NodeType != customb.NodeType
                        || customa.Resource.StructuralId != customb.Resource.StructuralId)
                        return false;
                    break;
            }
        }

        return true;
    }

    private static bool StagesMatch(ImmutableArray<FusedStage> a, ImmutableArray<FusedStage> b)
    {
        if (a.Length != b.Length)
            return false;

        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].GetType() != b[i].GetType())
                return false;

            if (a[i] is RuntimeShaderStage ra && b[i] is RuntimeShaderStage rb)
            {
                if (!string.Equals(ra.Source.IdentityHash, rb.Source.IdentityHash, StringComparison.Ordinal)
                    || ra.Children.Length != rb.Children.Length
                    || ra.SrcTileMode != rb.SrcTileMode)
                    return false;
            }
        }

        return true;
    }
}
