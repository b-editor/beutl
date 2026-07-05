using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>
/// One per-frame uniform value bound into a <see cref="ShaderNodeDescriptor"/> (feature 004, data-model §1).
/// A uniform's <em>name</em> is structural (it is part of the SKSL source), but its <em>value</em> is a
/// parameter — changing a value never recompiles the plan (A4). The fused executor prefixes the name with
/// <c>fe{N}_</c> when the node is merged into a shared program (see <see cref="SkslSnippetMerger"/>).
/// </summary>
public abstract record UniformBinding(string Name)
{
    /// <summary>Writes this value into <paramref name="builder"/> under <paramref name="effectiveName"/> (the possibly prefixed uniform name).</summary>
    internal abstract void Apply(SKRuntimeShaderBuilder builder, string effectiveName);
}

/// <summary>A scalar <c>float</c> uniform.</summary>
public sealed record FloatUniform(string Name, float Value) : UniformBinding(Name)
{
    internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName)
        => builder.Uniforms[effectiveName] = Value;
}

/// <summary>A <c>float2</c> uniform.</summary>
public sealed record Float2Uniform(string Name, float X, float Y) : UniformBinding(Name)
{
    internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName)
        => builder.Uniforms[effectiveName] = new[] { X, Y };
}

/// <summary>A <c>float3</c> uniform.</summary>
public sealed record Float3Uniform(string Name, float X, float Y, float Z) : UniformBinding(Name)
{
    internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName)
        => builder.Uniforms[effectiveName] = new[] { X, Y, Z };
}

/// <summary>A <c>float4</c> uniform.</summary>
public sealed record Float4Uniform(string Name, float X, float Y, float Z, float W) : UniformBinding(Name)
{
    internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName)
        => builder.Uniforms[effectiveName] = new[] { X, Y, Z, W };
}

/// <summary>An <c>int</c> uniform.</summary>
public sealed record IntUniform(string Name, int Value) : UniformBinding(Name)
{
    internal override void Apply(SKRuntimeShaderBuilder builder, string effectiveName)
        => builder.Uniforms[effectiveName] = Value;
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
        Add(new FloatUniform(Validate(name), value));
        return this;
    }

    /// <summary>Appends a <c>float2</c> uniform.</summary>
    public UniformBindingBuilder Float2(string name, float x, float y)
    {
        Add(new Float2Uniform(Validate(name), x, y));
        return this;
    }

    /// <summary>Appends a <c>float3</c> uniform.</summary>
    public UniformBindingBuilder Float3(string name, float x, float y, float z)
    {
        Add(new Float3Uniform(Validate(name), x, y, z));
        return this;
    }

    /// <summary>Appends a <c>float4</c> uniform.</summary>
    public UniformBindingBuilder Float4(string name, float x, float y, float z, float w)
    {
        Add(new Float4Uniform(Validate(name), x, y, z, w));
        return this;
    }

    /// <summary>Appends an <c>int</c> uniform.</summary>
    public UniformBindingBuilder Int(string name, int value)
    {
        Add(new IntUniform(Validate(name), value));
        return this;
    }

    private void Add(UniformBinding binding) => _bindings.Add(binding);

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
