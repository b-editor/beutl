using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>
/// One per-frame uniform value bound into a <see cref="ShaderNodeDescriptor"/> (feature 004, data-model §1).
/// A uniform's <em>name</em> is structural (it is part of the SKSL source), but its <em>value</em> is a
/// parameter — changing a value never recompiles the plan (A4). The fused executor prefixes the name with
/// <c>fe{N}_</c> when the node is merged into a shared program (see <see cref="SkslSnippetMerger"/>).
/// </summary>
/// <remarks>
/// This hierarchy is open: a plugin binds an unanticipated uniform shape by subclassing and overriding
/// <see cref="Apply"/>, or via the <see cref="RawUniform"/> escape hatch. An override must only write the
/// frame's <em>value</em> under <c>effectiveName</c> — it must never vary the shader source or the set of
/// names written per frame, or A4's structure/parameter separation breaks.
/// </remarks>
public abstract record UniformBinding(string Name)
{
    /// <summary>Writes this value into <paramref name="builder"/> under <paramref name="effectiveName"/> (the possibly prefixed uniform name).</summary>
    protected internal abstract void Apply(SKRuntimeShaderBuilder builder, string effectiveName);
}

/// <summary>A scalar <c>float</c> uniform.</summary>
public sealed record FloatUniform(string Name, float Value) : UniformBinding(Name)
{
    /// <inheritdoc/>
    protected internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName)
        => builder.Uniforms[effectiveName] = Value;
}

/// <summary>A <c>float2</c> uniform.</summary>
public sealed record Float2Uniform(string Name, float X, float Y) : UniformBinding(Name)
{
    /// <inheritdoc/>
    protected internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName)
        => builder.Uniforms[effectiveName] = new[] { X, Y };
}

/// <summary>A <c>float3</c> uniform.</summary>
public sealed record Float3Uniform(string Name, float X, float Y, float Z) : UniformBinding(Name)
{
    /// <inheritdoc/>
    protected internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName)
        => builder.Uniforms[effectiveName] = new[] { X, Y, Z };
}

/// <summary>A <c>float4</c> uniform.</summary>
public sealed record Float4Uniform(string Name, float X, float Y, float Z, float W) : UniformBinding(Name)
{
    /// <inheritdoc/>
    protected internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName)
        => builder.Uniforms[effectiveName] = new[] { X, Y, Z, W };
}

/// <summary>An <c>int</c> uniform.</summary>
public sealed record IntUniform(string Name, int Value) : UniformBinding(Name)
{
    /// <inheritdoc/>
    protected internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName)
        => builder.Uniforms[effectiveName] = Value;
}

/// <summary>A <c>float[]</c> uniform (SKSL fixed-size float array; the array length must match the declaration).</summary>
public sealed record FloatArrayUniform(string Name, float[] Values) : UniformBinding(Name)
{
    /// <inheritdoc/>
    protected internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName)
        => builder.Uniforms[effectiveName] = Values;
}

/// <summary>A <c>float3x3</c> uniform from a 2D <see cref="Matrix"/>.</summary>
public sealed record Matrix3x3Uniform(string Name, Matrix Value) : UniformBinding(Name)
{
    /// <inheritdoc/>
    protected internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName)
        => builder.Uniforms[effectiveName] = Value.ToSKMatrix();
}

/// <summary>
/// The escape hatch for uniform shapes this vocabulary does not anticipate: <paramref name="Writer"/> receives
/// the shader builder and the effective (possibly <c>fe{N}_</c>-prefixed) uniform name and writes the value
/// itself. The writer must only write the frame's value under that name — never mutate children or vary the set
/// of names it writes per frame (A4's structure/parameter separation).
/// </summary>
public sealed record RawUniform(string Name, Action<SKRuntimeShaderBuilder, string> Writer) : UniformBinding(Name)
{
    /// <inheritdoc/>
    protected internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName)
        => Writer(builder, effectiveName);
}

/// <summary>
/// Fluent collector for a shader's uniform bindings, matching the quickstart shape
/// <c>uniforms: u =&gt; u.Float("gamma", r.Gamma)</c>. Each call appends a binding; ordering is preserved.
/// </summary>
public sealed class UniformBindingBuilder
{
    private readonly List<UniformBinding> _bindings = [];

    /// <summary>Appends a <c>float</c> uniform.</summary>
    public UniformBindingBuilder Float(string name, float value)
    {
        _bindings.Add(new FloatUniform(Validate(name), value));
        return this;
    }

    /// <summary>Appends a <c>float2</c> uniform.</summary>
    public UniformBindingBuilder Float2(string name, float x, float y)
    {
        _bindings.Add(new Float2Uniform(Validate(name), x, y));
        return this;
    }

    /// <summary>Appends a <c>float3</c> uniform.</summary>
    public UniformBindingBuilder Float3(string name, float x, float y, float z)
    {
        _bindings.Add(new Float3Uniform(Validate(name), x, y, z));
        return this;
    }

    /// <summary>Appends a <c>float4</c> uniform.</summary>
    public UniformBindingBuilder Float4(string name, float x, float y, float z, float w)
    {
        _bindings.Add(new Float4Uniform(Validate(name), x, y, z, w));
        return this;
    }

    /// <summary>Appends an <c>int</c> uniform.</summary>
    public UniformBindingBuilder Int(string name, int value)
    {
        _bindings.Add(new IntUniform(Validate(name), value));
        return this;
    }

    /// <summary>Appends a <c>float[]</c> uniform. The array length must match the SKSL declaration.</summary>
    public UniformBindingBuilder FloatArray(string name, float[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _bindings.Add(new FloatArrayUniform(Validate(name), values));
        return this;
    }

    /// <summary>Appends a <c>float3x3</c> uniform from a 2D <see cref="Matrix"/>.</summary>
    public UniformBindingBuilder Matrix3x3(string name, Matrix value)
    {
        _bindings.Add(new Matrix3x3Uniform(Validate(name), value));
        return this;
    }

    /// <summary>Appends a <see cref="RawUniform"/> escape-hatch binding (see its contract).</summary>
    public UniformBindingBuilder Raw(string name, Action<SKRuntimeShaderBuilder, string> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _bindings.Add(new RawUniform(Validate(name), writer));
        return this;
    }

    /// <summary>Appends a pre-constructed binding (e.g. a plugin-defined <see cref="UniformBinding"/> subclass).</summary>
    public UniformBindingBuilder Add(UniformBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
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
