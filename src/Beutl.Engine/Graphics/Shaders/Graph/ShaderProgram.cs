using System.Collections.Immutable;
using Beutl.Graphics.Backend;

namespace Beutl.Graphics.Shaders;

/// <summary>The language of an authored shader source.</summary>
public enum ShaderLanguage
{
    Glsl,
}

/// <summary>Immutable source text. Constructing a source does not compile it or allocate device resources.</summary>
public sealed record ShaderSource
{
    public ShaderSource(string text, ShaderLanguage language = ShaderLanguage.Glsl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (!Enum.IsDefined(language))
            throw new ArgumentOutOfRangeException(nameof(language));
        Text = text;
        Language = language;
    }

    public string Text { get; }

    public ShaderLanguage Language { get; }
}

/// <summary>An immutable program definition, independent of a device and of per-request parameter values.</summary>
public sealed class ShaderProgram
{
    private ShaderProgram(ImmutableDictionary<ShaderStage, ShaderSource> stages) => Stages = stages;

    public ImmutableDictionary<ShaderStage, ShaderSource> Stages { get; }

    internal bool IsCompute => Stages.ContainsKey(ShaderStage.Compute);

    public static ShaderProgram Graphics(ShaderSource vertex, ShaderSource fragment)
        => Graphics(vertex, fragment, null, null, null);

    /// <summary>Adds optional geometry or paired tessellation stages to a graphics program.</summary>
    public static ShaderProgram Graphics(ShaderSource vertex, ShaderSource fragment, ShaderSource? geometry = null,
        ShaderSource? tessellationControl = null, ShaderSource? tessellationEvaluation = null)
    {
        ArgumentNullException.ThrowIfNull(vertex);
        ArgumentNullException.ThrowIfNull(fragment);
        if ((tessellationControl is null) != (tessellationEvaluation is null))
            throw new ArgumentException("Tessellation control and evaluation stages must be provided together.");
        var stages = ImmutableDictionary.CreateBuilder<ShaderStage, ShaderSource>();
        stages.Add(ShaderStage.Vertex, vertex);
        stages.Add(ShaderStage.Fragment, fragment);
        if (geometry is not null) stages.Add(ShaderStage.Geometry, geometry);
        if (tessellationControl is not null) stages.Add(ShaderStage.TessellationControl, tessellationControl);
        if (tessellationEvaluation is not null) stages.Add(ShaderStage.TessellationEvaluation, tessellationEvaluation);
        return new(stages.ToImmutable());
    }

    public static ShaderProgram Compute(ShaderSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new(ImmutableDictionary<ShaderStage, ShaderSource>.Empty.Add(ShaderStage.Compute, source));
    }

    /// <summary>Uses a fullscreen triangle with a location-0 UV varying ranging from zero to one.</summary>
    public static ShaderProgram Fullscreen(ShaderSource fragment) => Graphics(s_fullscreenVertex, fragment);

    private static readonly ShaderSource s_fullscreenVertex = new("""
        #version 450
        layout(location = 0) out vec2 uv;
        void main() {
            vec2 p = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
            uv = p;
            gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);
        }
        """);
}
