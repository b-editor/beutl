using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>
/// A named child shader bound into a <see cref="ShaderNodeDescriptor"/> beyond the implicit <c>src</c> input
/// (feature 004, data-model §1). The child's <em>name</em> is structural (part of the SKSL source, honored by the
/// snippet merger's prefixing); the shader instance is a per-frame parameter — a swap re-binds without recompiling
/// (A4).
/// </summary>
/// <remarks>
/// A child comes in an <b>eager</b> form (a shader already built at describe time) and a <b>deferred</b> form
/// (<see cref="Deferred"/>, a factory that produces the shader at execution time from the pass's
/// <see cref="PassUniformContext"/>).
/// <para>
/// A <b>sampler</b> is the eager form used for an invariance-safe value lookup — a LUT or curve texture indexed by
/// the source pixel's <em>colour</em>, not its position — which is why a fusable snippet may carry one (build it
/// with <see cref="EffectGraphBuilder.Sampler(string, SKShader)"/>; the snippet path rejects the coordinate-
/// dependent deferred form). Pick the deferred form whenever the shader's construction depends on the pass's device
/// density or buffer size — a cross-sampled map is evaluated in device space with no canvas density transform, so a
/// describe-time bake mis-scales the lookup when the resource-resolution re-clamp (execution-plan §C3.2) executes
/// the pass below its describe-time working scale (the child analogue of the A4 late-bound-uniform rule).
/// </para>
/// Ownership follows the form:
/// <list type="bullet">
/// <item><description>
/// <b>Eager, graph-scoped</b> — build it with <see cref="EffectGraphBuilder.Child(string, SKShader)"/> or
/// <see cref="EffectGraphBuilder.Sampler(string, SKShader)"/> from inside <c>FilterEffect.Describe</c>. The graph
/// owns the shader and disposes it once the frame's plan has executed (even if the pass is skipped for an empty
/// ROI), so you MUST NOT dispose it yourself. Use this for a shader you build fresh in <c>Describe</c> every
/// frame — the normal case, and what the in-tree effects do.
/// </description></item>
/// <item><description>
/// <b>Eager, caller-owned</b> — construct this record directly (<c>new ChildBinding(name, shader)</c>). YOU keep
/// ownership and dispose the shader yourself (e.g. when your cross-frame cache evicts it); the graph will not free
/// it. Use this for a shader you cache and reuse across frames. This is a supported mode, not a leak.
/// </description></item>
/// <item><description>
/// <b>Deferred, executor-owned</b> — build it with <see cref="Deferred"/>. The factory runs once per pass
/// execution and the executor disposes each product after that pass's draw, so the shader always resolves against
/// the pass's real density and is never leaked. The factory MUST return a fresh, disposable shader every call and
/// MUST NOT vary the child NAME (structure stays fixed; only the shader value is late-resolved).
/// </description></item>
/// </list>
/// </remarks>
public sealed record ChildBinding
{
    private readonly Func<PassUniformContext, SKShader>? _factory;

    /// <summary>Creates an eager binding whose <paramref name="shader"/> is already built (see the ownership modes).</summary>
    public ChildBinding(string name, SKShader shader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(shader);
        Name = name;
        Shader = shader;
    }

    private ChildBinding(string name, Func<PassUniformContext, SKShader> factory)
    {
        Name = name;
        _factory = factory;
    }

    /// <summary>The structural child name (part of the SKSL source, honored by the snippet merger's prefixing).</summary>
    public string Name { get; }

    /// <summary>
    /// The pre-built shader of an eager binding, or <see langword="null"/> for a <see cref="Deferred"/> binding
    /// (whose shader does not exist until execution time).
    /// </summary>
    public SKShader? Shader { get; }

    /// <summary>
    /// Whether this is a <see cref="Deferred"/> binding (its shader is built per-pass at execution time and is
    /// therefore coordinate/density-dependent). A fusable snippet accepts only eager bindings, so the snippet path
    /// rejects a deferred child.
    /// </summary>
    internal bool IsDeferred => _factory is not null;

    /// <summary>
    /// Creates a deferred binding whose shader is produced by <paramref name="factory"/> at pass execution, from
    /// the pass's execution-time <see cref="PassUniformContext"/> (real <c>WorkingScale</c> / buffer size). The
    /// executor disposes each per-pass product after the pass's draw, so a factory MUST return a fresh, owned,
    /// disposable shader every call. Use this for a density- or size-dependent child (e.g. a cross-sampled
    /// displacement map) so its construction tracks the EXECUTION density, never the describe-time working scale (A4).
    /// </summary>
    public static ChildBinding Deferred(string name, Func<PassUniformContext, SKShader> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        return new ChildBinding(name, factory);
    }

    /// <summary>
    /// Resolves the shader to bind for this pass. An eager binding returns its pre-built shader with
    /// <paramref name="executorOwned"/> <see langword="false"/> (owned by the graph or the caller — the executor
    /// must not dispose it); a deferred binding invokes its factory and returns the fresh product with
    /// <paramref name="executorOwned"/> <see langword="true"/> (the executor disposes it after the pass's draw).
    /// </summary>
    internal SKShader Resolve(in PassUniformContext context, out bool executorOwned)
    {
        if (_factory is not null)
        {
            executorOwned = true;
            return _factory(context);
        }

        executorOwned = false;
        return Shader!;
    }
}
