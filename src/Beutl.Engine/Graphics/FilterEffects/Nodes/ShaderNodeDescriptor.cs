using System.Collections.Immutable;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>
/// An SKSL shader node (feature 004, data-model §1, contract A2). Two forms:
/// <list type="bullet">
/// <item><description><b>Snippet</b> — defines <c>half4 apply(half4 c)</c>, is coordinate-invariant, and fuses
/// with adjacent invariant nodes into one draw. <c>c</c> is premultiplied-alpha, linear-light (the
/// RGBA16F/<c>SrgbLinear</c>/<c>Premul</c> working format) and the return must be too; unpremultiply/
/// re-premultiply internally if straight alpha is needed (as today's <c>Gamma</c>/<c>Curves</c>/LUT SKSL does).</description></item>
/// <item><description><b>Whole-source</b> — defines <c>half4 main(float2 coord)</c> with a <c>src</c> child
/// (today's <see cref="SKSLShader"/> convention); its own pass unless the author opts into invariance.</description></item>
/// </list>
/// The <see cref="SkslSource"/> is structural; uniform values, sampler contents and child shaders are parameters (A4).
/// </summary>
public sealed record ShaderNodeDescriptor : EffectNodeDescriptor
{
    private ShaderNodeDescriptor(
        SkslSource source,
        bool isCoordinateInvariant,
        BoundsContract bounds,
        ImmutableArray<UniformBinding> uniforms,
        ImmutableArray<ChildBinding> children,
        SKShaderTileMode srcTileMode)
    {
        Source = source;
        IsCoordinateInvariant = isCoordinateInvariant;
        Bounds = bounds;
        Uniforms = uniforms;
        Children = children;
        SrcTileMode = srcTileMode;
    }

    /// <summary>The identity-hashable SKSL source (snippet or whole-source).</summary>
    public SkslSource Source { get; }

    /// <inheritdoc/>
    public override bool IsCoordinateInvariant { get; }

    /// <inheritdoc/>
    public override BoundsContract Bounds { get; }

    /// <summary>Per-frame uniform values (names structural, values parameters).</summary>
    public ImmutableArray<UniformBinding> Uniforms { get; }

    /// <summary>
    /// Extra child shaders bound by name beyond the implicit <c>src</c> input (a LUT/curve sampler, a whole-source
    /// shader's displacement map). Each name is structural; the shader instance is a parameter (A4).
    /// </summary>
    public ImmutableArray<ChildBinding> Children { get; }

    /// <summary>Tile mode for the implicit <c>src</c> child (whole-source only); <c>Decal</c> for snippet nodes.</summary>
    public SKShaderTileMode SrcTileMode { get; }

    /// <summary>
    /// Builds a fusable coordinate-invariant snippet node from a <c>half4 apply(half4 c)</c> source. Bounds are
    /// identity by construction (A3). Optionally binds uniforms and <paramref name="samplers"/> — eager child
    /// shaders used as invariance-safe value lookups (a LUT/curve texture indexed by the pixel's colour, not its
    /// position). A coordinate-dependent deferred child (<see cref="ChildBinding.Deferred"/>) would break the
    /// snippet's coordinate invariance and is rejected.
    /// </summary>
    public static ShaderNodeDescriptor Snippet(
        string source,
        Action<UniformBindingBuilder>? uniforms = null,
        IEnumerable<ChildBinding>? samplers = null)
    {
        ImmutableArray<ChildBinding> children = ValidateChildren(samplers, reserveSourceName: false);
        foreach (ChildBinding child in children)
        {
            if (child.IsDeferred)
            {
                throw new ArgumentException(
                    $"A fusable snippet cannot bind the deferred (coordinate-dependent) child '{child.Name}': a "
                    + "snippet samples only the current pixel, so it accepts only eager samplers (a LUT/curve "
                    + "texture indexed by colour). Use a whole-source shader node for a deferred child (A2/A4).",
                    nameof(samplers));
            }
        }

        return new ShaderNodeDescriptor(
            SkslSource.Snippet(source),
            isCoordinateInvariant: true,
            BoundsContract.Identity,
            UniformBindingBuilder.Collect(uniforms).ToImmutableArray(),
            children,
            SKShaderTileMode.Decal);
    }

    /// <summary>
    /// Builds a non-invariant whole-source shader node defining <c>half4 main(float2 coord)</c> with a
    /// <c>src</c> child. Runs as its own pass; <paramref name="bounds"/> declares its forward/backward
    /// bounds. A shader that provably samples only the current pixel uses
    /// <see cref="WholeSourceInvariant"/> instead — invariance and a custom bounds contract are mutually
    /// exclusive by construction.
    /// </summary>
    public static ShaderNodeDescriptor WholeSource(
        string source,
        BoundsContract bounds,
        Action<UniformBindingBuilder>? uniforms = null,
        IEnumerable<ChildBinding>? children = null,
        SKShaderTileMode srcTileMode = SKShaderTileMode.Decal)
    {
        bounds.ThrowIfUninitialized(nameof(bounds));
        return new ShaderNodeDescriptor(
            SkslSource.WholeSource(source),
            isCoordinateInvariant: false,
            bounds,
            UniformBindingBuilder.Collect(uniforms).ToImmutableArray(),
            ValidateChildren(children, reserveSourceName: true),
            srcTileMode);
    }

    /// <summary>
    /// Builds a coordinate-invariant whole-source shader node (A6's opt-in): the author asserts the shader
    /// samples only the current pixel, so it gets identity bounds by construction — invariance drives sizing and
    /// ROI (the pass never grows or re-anchors), but the node still runs as its own pass: only
    /// <see cref="Snippet"/>-kind shaders merge into a fused program. Violating the invariance assertion
    /// produces wrong output by contract (A3).
    /// </summary>
    public static ShaderNodeDescriptor WholeSourceInvariant(
        string source,
        Action<UniformBindingBuilder>? uniforms = null,
        IEnumerable<ChildBinding>? children = null,
        SKShaderTileMode srcTileMode = SKShaderTileMode.Decal)
    {
        return new ShaderNodeDescriptor(
            SkslSource.WholeSource(source),
            isCoordinateInvariant: true,
            BoundsContract.Identity,
            UniformBindingBuilder.Collect(uniforms).ToImmutableArray(),
            ValidateChildren(children, reserveSourceName: true),
            srcTileMode);
    }

    // The executor binds children by NAME into the shared runtime builder, so a duplicate name silently replaces
    // the earlier binding — and a whole-source child named 'src' would replace the implicit upstream source the
    // executor binds under that name before the descriptor's own children.
    private static ImmutableArray<ChildBinding> ValidateChildren(IEnumerable<ChildBinding>? children, bool reserveSourceName)
    {
        ImmutableArray<ChildBinding> array = (children ?? []).ToImmutableArray();
        for (int i = 0; i < array.Length; i++)
        {
            ChildBinding child = array[i];
            if (reserveSourceName && child.Name == Rendering.SkslSnippetMerger.SourceChildName)
            {
                throw new ArgumentException(
                    $"'{Rendering.SkslSnippetMerger.SourceChildName}' is the reserved implicit source child of a "
                    + "whole-source shader; an extra child bound under it would silently replace the effect input.",
                    nameof(children));
            }

            for (int j = 0; j < i; j++)
            {
                if (array[j].Name == child.Name)
                {
                    throw new ArgumentException(
                        $"Duplicate child binding name '{child.Name}': children bind by name, so the later binding "
                        + "would silently replace the earlier one.",
                        nameof(children));
                }
            }
        }

        return array;
    }
}
