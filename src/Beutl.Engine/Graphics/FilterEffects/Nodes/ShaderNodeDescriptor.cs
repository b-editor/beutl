using System.Collections.Immutable;

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
        ImmutableArray<SamplerBinding> samplers,
        ImmutableArray<ChildBinding> children)
    {
        Source = source;
        IsCoordinateInvariant = isCoordinateInvariant;
        Bounds = bounds;
        Uniforms = uniforms;
        Samplers = samplers;
        Children = children;
    }

    /// <summary>The identity-hashable SKSL source (snippet or whole-source).</summary>
    public SkslSource Source { get; }

    /// <inheritdoc/>
    public override bool IsCoordinateInvariant { get; }

    /// <inheritdoc/>
    public override BoundsContract Bounds { get; }

    /// <summary>Per-frame uniform values (names structural, values parameters).</summary>
    public ImmutableArray<UniformBinding> Uniforms { get; }

    /// <summary>Extra texture samplers bound by name (e.g. a LUT).</summary>
    public ImmutableArray<SamplerBinding> Samplers { get; }

    /// <summary>Extra child shaders bound by name (whole-source only, beyond the implicit <c>src</c>).</summary>
    public ImmutableArray<ChildBinding> Children { get; }

    /// <inheritdoc/>
    public override EffectNodeKind Kind => EffectNodeKind.Shader;

    /// <summary>
    /// Builds a fusable coordinate-invariant snippet node from a <c>half4 apply(half4 c)</c> source. Bounds are
    /// identity by construction (A3). Optionally binds uniforms and samplers.
    /// </summary>
    public static ShaderNodeDescriptor Snippet(
        string source,
        Action<UniformBindingBuilder>? uniforms = null,
        IEnumerable<SamplerBinding>? samplers = null)
    {
        return new ShaderNodeDescriptor(
            SkslSource.Snippet(source),
            isCoordinateInvariant: true,
            BoundsContract.Identity,
            UniformBindingBuilder.Collect(uniforms).ToImmutableArray(),
            (samplers ?? []).ToImmutableArray(),
            children: []);
    }

    /// <summary>
    /// Builds a whole-source shader node defining <c>half4 main(float2 coord)</c> with a <c>src</c> child.
    /// Non-invariant by default (its own pass); pass <paramref name="isCoordinateInvariant"/> <see langword="true"/>
    /// only when the shader provably samples solely the current pixel, in which case identity bounds are used and
    /// the node becomes fusable (A6's opt-in).
    /// </summary>
    public static ShaderNodeDescriptor WholeSource(
        string source,
        BoundsContract bounds,
        Action<UniformBindingBuilder>? uniforms = null,
        IEnumerable<SamplerBinding>? samplers = null,
        IEnumerable<ChildBinding>? children = null,
        bool isCoordinateInvariant = false)
    {
        return new ShaderNodeDescriptor(
            SkslSource.WholeSource(source),
            isCoordinateInvariant,
            isCoordinateInvariant ? BoundsContract.Identity : bounds,
            UniformBindingBuilder.Collect(uniforms).ToImmutableArray(),
            (samplers ?? []).ToImmutableArray(),
            (children ?? []).ToImmutableArray());
    }
}
