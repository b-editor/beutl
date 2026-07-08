using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>
/// An extra texture sampler bound into a <see cref="ShaderNodeDescriptor"/> (e.g. a LUT), feature 004
/// data-model §1. The sampler's <em>name</em> is structural; the texture <em>contents</em> are a parameter
/// (a LUT swap re-binds without recompiling, A4). Held as an <see cref="SKShader"/> the fused executor wires as a
/// child shader.
/// </summary>
/// <remarks>
/// Build one with <see cref="EffectGraphBuilder.Sampler(string, SKShader)"/> from inside
/// <c>FilterEffect.Describe</c> so the <em>graph</em> owns the shader's lifetime: it is disposed once when the
/// frame's plan has executed (even if the pass is skipped for an empty ROI), so an author MUST NOT dispose it
/// themselves. Constructing this record directly leaves the shader unmanaged — the graph will not free it.
/// </remarks>
public sealed record SamplerBinding(string Name, SKShader Shader);

/// <summary>
/// A child shader bound into a whole-source <see cref="ShaderNodeDescriptor"/> beyond the implicit <c>src</c>
/// input (feature 004, data-model §1). The name is structural; the shader instance is per-frame.
/// </summary>
/// <remarks>
/// Build one with <see cref="EffectGraphBuilder.Child(string, SKShader)"/> so the graph owns and disposes the
/// shader once per frame; do not dispose it yourself. Constructing this record directly leaves the shader
/// unmanaged.
/// </remarks>
public sealed record ChildBinding(string Name, SKShader Shader);
