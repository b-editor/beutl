using Beutl.Graphics.Rendering;
using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>
/// The execution-time context handed to every <see cref="UniformBinding.Apply"/> so density- and size-dependent
/// uniform <em>values</em> are computed against the pass's REAL resolution (feature 004, contract A4/A5). The
/// resource-resolution re-clamp (execution-plan §C3.2, <c>ClampWorkingScaleToBufferBudget</c>) can drive a pass's
/// working density BELOW the describe-time <see cref="EffectGraphBuilder.WorkingScale"/> when a forward-inflated
/// buffer crosses the per-axis budget, and the monotonic carry can lower it for a later pass in the same graph.
/// A uniform whose device-space value depends on that density (a shifted sample offset, a pivot, a resolution)
/// MUST be late-bound from this context, never frozen at describe time — describe-time <c>WorkingScale</c> is only
/// valid for logical→device conversions that feed BOUNDS.
/// </summary>
/// <param name="WorkingScale">The pass's actual resolved working density (device px per logical px).</param>
/// <param name="TargetWidth">The pass output buffer width in device px.</param>
/// <param name="TargetHeight">The pass output buffer height in device px.</param>
/// <param name="TargetBounds">
/// The exact logical bounds baked by this pass. For fan-out execution this is the current branch's bounds, not the
/// graph-wide describe-time bounds. Use its size for branch-local pivots and coordinate-sampled child shaders.
/// </param>
/// <param name="Diagnostics">
/// The owning renderer's effect-pipeline counters (or <see langword="null"/> when unobserved), forwarded so a
/// deferred child that renders a <see cref="DrawableBrush"/> map keeps that nested render observable on
/// <c>IRenderer.Diagnostics</c> (FR-017). Observation only — the executor still owns every buffer decision.
/// </param>
/// <param name="RenderIntent">Explicit preview/delivery failure policy for deferred child rendering.</param>
/// <param name="PullPurpose">The pull purpose for deferred child rendering.</param>
public readonly record struct PassUniformContext
{
    public PassUniformContext(
        float WorkingScale,
        int TargetWidth,
        int TargetHeight,
        Rect TargetBounds,
        RenderIntent RenderIntent,
        RenderPullPurpose PullPurpose,
        PipelineDiagnostics? Diagnostics = null)
    {
        this.WorkingScale = WorkingScale;
        this.TargetWidth = TargetWidth;
        this.TargetHeight = TargetHeight;
        this.TargetBounds = TargetBounds;
        this.RenderIntent = RenderPolicyValidation.Validate(RenderIntent, nameof(RenderIntent));
        this.PullPurpose = RenderPolicyValidation.Validate(PullPurpose, nameof(PullPurpose));
        this.Diagnostics = Diagnostics;
    }

    public float WorkingScale { get; }

    public int TargetWidth { get; }

    public int TargetHeight { get; }

    public Rect TargetBounds { get; }

    public RenderIntent RenderIntent { get; }

    public RenderPullPurpose PullPurpose { get; }

    public PipelineDiagnostics? Diagnostics { get; }

    public void Deconstruct(
        out float WorkingScale,
        out int TargetWidth,
        out int TargetHeight,
        out Rect TargetBounds,
        out RenderIntent RenderIntent,
        out RenderPullPurpose PullPurpose,
        out PipelineDiagnostics? Diagnostics)
    {
        WorkingScale = this.WorkingScale;
        TargetWidth = this.TargetWidth;
        TargetHeight = this.TargetHeight;
        TargetBounds = this.TargetBounds;
        RenderIntent = this.RenderIntent;
        PullPurpose = this.PullPurpose;
        Diagnostics = this.Diagnostics;
    }
}

/// <summary>
/// One per-frame uniform value bound into a <see cref="ShaderNodeDescriptor"/> (feature 004, data-model §1).
/// A uniform's <em>name</em> is structural (it is part of the SKSL source), but its <em>value</em> is a
/// parameter — changing a value never recompiles the plan (A4). The fused executor prefixes the name with
/// <c>fe{N}_</c> when the node is merged into a shared program (see <see cref="SkslSnippetMerger"/>).
/// </summary>
/// <remarks>
/// This hierarchy is open: a plugin binds an unanticipated uniform shape by subclassing and overriding
/// <see cref="Apply"/>, or via the <see cref="RawUniform"/> / <see cref="DeferredUniform"/> escape hatches. An
/// override must only write the frame's <em>value</em> under <c>effectiveName</c> — it must never vary the shader
/// source or the set of names written per frame, or A4's structure/parameter separation breaks. A value that
/// depends on the pass's device density or buffer size MUST derive it from the supplied
/// <see cref="PassUniformContext"/> (execution-time), never from the describe-time working scale.
/// </remarks>
public abstract record UniformBinding(string Name)
{
    /// <summary>
    /// Writes this value into <paramref name="builder"/> under <paramref name="effectiveName"/> (the possibly
    /// prefixed uniform name), using <paramref name="context"/> for any density- or size-dependent value.
    /// </summary>
    protected internal abstract void Apply(
        SKRuntimeShaderBuilder builder, string effectiveName, in PassUniformContext context);

    /// <summary>
    /// Writes a short-lived vector immediately. The scoped parameter lets callers use stack storage while
    /// <see cref="SKRuntimeEffectUniforms.Add"/> copies the samples into Skia's owned uniform buffer.
    /// </summary>
    protected static void ApplyFloatVector(
        SKRuntimeShaderBuilder builder, string effectiveName, scoped ReadOnlySpan<float> values)
        => builder.Uniforms.Add(effectiveName, values);
}

/// <summary>A scalar <c>float</c> uniform.</summary>
public sealed record FloatUniform(string Name, float Value) : UniformBinding(Name)
{
    /// <inheritdoc/>
    protected internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName, in PassUniformContext context)
        => builder.Uniforms[effectiveName] = Value;
}

/// <summary>A <c>float2</c> uniform.</summary>
public sealed record Float2Uniform(string Name, float X, float Y) : UniformBinding(Name)
{
    /// <inheritdoc/>
    protected internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName, in PassUniformContext context)
    {
        Span<float> values = stackalloc float[] { X, Y };
        ApplyFloatVector(builder, effectiveName, values);
    }
}

/// <summary>A <c>float3</c> uniform.</summary>
public sealed record Float3Uniform(string Name, float X, float Y, float Z) : UniformBinding(Name)
{
    /// <inheritdoc/>
    protected internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName, in PassUniformContext context)
    {
        Span<float> values = stackalloc float[] { X, Y, Z };
        ApplyFloatVector(builder, effectiveName, values);
    }
}

/// <summary>A <c>float4</c> uniform.</summary>
public sealed record Float4Uniform(string Name, float X, float Y, float Z, float W) : UniformBinding(Name)
{
    /// <inheritdoc/>
    protected internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName, in PassUniformContext context)
    {
        Span<float> values = stackalloc float[] { X, Y, Z, W };
        ApplyFloatVector(builder, effectiveName, values);
    }
}

/// <summary>An <c>int</c> uniform.</summary>
public sealed record IntUniform(string Name, int Value) : UniformBinding(Name)
{
    /// <inheritdoc/>
    protected internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName, in PassUniformContext context)
        => builder.Uniforms[effectiveName] = Value;
}

/// <summary>A <c>float[]</c> uniform (SKSL fixed-size float array; the array length must match the declaration).</summary>
public sealed record FloatArrayUniform(string Name, float[] Values) : UniformBinding(Name)
{
    /// <summary>The values written to the fixed-size SKSL array uniform.</summary>
    public float[] Values { get; init; } = Values ?? throw new ArgumentNullException(nameof(Values));

    /// <inheritdoc/>
    protected internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName, in PassUniformContext context)
        => builder.Uniforms[effectiveName] = Values;
}

/// <summary>A <c>float3x3</c> uniform from a 2D <see cref="Matrix"/>.</summary>
public sealed record Matrix3x3Uniform(string Name, Matrix Value) : UniformBinding(Name)
{
    /// <inheritdoc/>
    protected internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName, in PassUniformContext context)
        => builder.Uniforms[effectiveName] = Value.ToSKMatrix();
}

/// <summary>
/// A <c>float2</c> uniform whose logical value is scaled to device px by the pass's actual working density at
/// execution (A4/A5): the value written is <c>(LogicalX, LogicalY) * context.WorkingScale</c>. Use this for any
/// device-space offset, translation or pivot whose describe-time logical value must be converted with the
/// EXECUTION density, not the describe-time working scale — the two differ when the resource-resolution re-clamp
/// lowers a pass's density (execution-plan §C3.2).
/// </summary>
public sealed record DensityScaledFloat2Uniform(string Name, float LogicalX, float LogicalY) : UniformBinding(Name)
{
    /// <inheritdoc/>
    protected internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName, in PassUniformContext context)
    {
        Span<float> values = stackalloc float[]
        {
            LogicalX * context.WorkingScale,
            LogicalY * context.WorkingScale,
        };
        ApplyFloatVector(builder, effectiveName, values);
    }
}

/// <summary>
/// The escape hatch for uniform shapes this vocabulary does not anticipate: <paramref name="Writer"/> receives
/// the shader builder and the effective (possibly <c>fe{N}_</c>-prefixed) uniform name and writes the value
/// itself. The writer must only write the frame's value under that name — never mutate children or vary the set
/// of names it writes per frame (A4's structure/parameter separation). Use <see cref="DeferredUniform"/> when the
/// value depends on the pass's device density or buffer size.
/// </summary>
public sealed record RawUniform(string Name, Action<SKRuntimeShaderBuilder, string> Writer) : UniformBinding(Name)
{
    /// <inheritdoc/>
    protected internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName, in PassUniformContext context)
        => Writer(builder, effectiveName);
}

/// <summary>
/// The late-bound escape hatch: <paramref name="Writer"/> receives the shader builder, the effective (possibly
/// <c>fe{N}_</c>-prefixed) uniform name, and the <see cref="PassUniformContext"/> so it can compute a value from
/// the pass's EXECUTION-time working density and buffer size (A4/A5). Same discipline as <see cref="RawUniform"/>:
/// write only the frame's value under that name, never vary the shader source or the set of names written.
/// </summary>
public sealed record DeferredUniform(string Name, Action<SKRuntimeShaderBuilder, string, PassUniformContext> Writer)
    : UniformBinding(Name)
{
    /// <inheritdoc/>
    protected internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName, in PassUniformContext context)
        => Writer(builder, effectiveName, context);
}

/// <summary>
/// Fluent collector for a shader's uniform bindings, matching the quickstart shape
/// <c>uniforms: u =&gt; u.Float("gamma", r.Gamma)</c>. Each call appends a binding; ordering is preserved.
/// </summary>
public sealed class UniformBindingBuilder
{
    private readonly List<UniformBinding> _bindings = [];
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    /// <summary>Appends a <c>float</c> uniform.</summary>
    public UniformBindingBuilder Float(string name, float value)
    {
        return AddBinding(new FloatUniform(Validate(name), value));
    }

    /// <summary>Appends a <c>float2</c> uniform.</summary>
    public UniformBindingBuilder Float2(string name, float x, float y)
    {
        return AddBinding(new Float2Uniform(Validate(name), x, y));
    }

    /// <summary>Appends a <c>float3</c> uniform.</summary>
    public UniformBindingBuilder Float3(string name, float x, float y, float z)
    {
        return AddBinding(new Float3Uniform(Validate(name), x, y, z));
    }

    /// <summary>Appends a <c>float4</c> uniform.</summary>
    public UniformBindingBuilder Float4(string name, float x, float y, float z, float w)
    {
        return AddBinding(new Float4Uniform(Validate(name), x, y, z, w));
    }

    /// <summary>Appends an <c>int</c> uniform.</summary>
    public UniformBindingBuilder Int(string name, int value)
    {
        return AddBinding(new IntUniform(Validate(name), value));
    }

    /// <summary>Appends a <c>float[]</c> uniform. The array length must match the SKSL declaration.</summary>
    public UniformBindingBuilder FloatArray(string name, float[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return AddBinding(new FloatArrayUniform(Validate(name), values));
    }

    /// <summary>Appends a <c>float3x3</c> uniform from a 2D <see cref="Matrix"/>.</summary>
    public UniformBindingBuilder Matrix3x3(string name, Matrix value)
    {
        return AddBinding(new Matrix3x3Uniform(Validate(name), value));
    }

    /// <summary>
    /// Appends a <c>float2</c> uniform whose logical value is scaled by the pass's execution-time working density
    /// (<see cref="DensityScaledFloat2Uniform"/>). Prefer this over <c>Float2(name, x * w, y * w)</c> for a
    /// device-space offset/pivot so the value tracks the resolved density rather than the describe-time one.
    /// </summary>
    public UniformBindingBuilder DensityScaledFloat2(string name, float logicalX, float logicalY)
    {
        return AddBinding(new DensityScaledFloat2Uniform(Validate(name), logicalX, logicalY));
    }

    /// <summary>Appends a <see cref="RawUniform"/> escape-hatch binding (see its contract).</summary>
    public UniformBindingBuilder Raw(string name, Action<SKRuntimeShaderBuilder, string> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        return AddBinding(new RawUniform(Validate(name), writer));
    }

    /// <summary>Appends a <see cref="DeferredUniform"/> late-bound escape-hatch binding (see its contract).</summary>
    public UniformBindingBuilder Deferred(string name, Action<SKRuntimeShaderBuilder, string, PassUniformContext> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        return AddBinding(new DeferredUniform(Validate(name), writer));
    }

    /// <summary>Appends a pre-constructed binding (e.g. a plugin-defined <see cref="UniformBinding"/> subclass).</summary>
    public UniformBindingBuilder Add(UniformBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        Validate(binding.Name);
        return AddBinding(binding);
    }

    private UniformBindingBuilder AddBinding(UniformBinding binding)
    {
        if (!_names.Add(binding.Name))
        {
            throw new ArgumentException(
                $"Duplicate uniform binding name '{binding.Name}': uniforms bind by name, so a later value would "
                + "silently replace the earlier binding.", nameof(binding));
        }

        _bindings.Add(binding);
        return this;
    }

    private static string Validate(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name;
    }

    internal UniformBinding[] Build() => _bindings.ToArray();

    internal static UniformBinding[] Collect(Action<UniformBindingBuilder>? configure)
    {
        if (configure == null)
            return [];

        var builder = new UniformBindingBuilder();
        configure(builder);
        return builder.Build();
    }
}
