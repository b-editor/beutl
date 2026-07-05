using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>
/// An extra texture sampler bound into a <see cref="ShaderNodeDescriptor"/> (e.g. a LUT), feature 004
/// data-model §1. The sampler's <em>name</em> is structural; the texture <em>contents</em> are a parameter
/// (a LUT swap re-binds without recompiling, A4). Held as an <see cref="SKShader"/> so the fused executor can
/// wire it as a child shader; disposal of the shader is the executor's responsibility for the frame.
/// </summary>
public sealed record SamplerBinding(string Name, SKShader Shader);

/// <summary>
/// A child shader bound into a whole-source <see cref="ShaderNodeDescriptor"/> beyond the implicit <c>src</c>
/// input (feature 004, data-model §1). The name is structural; the shader instance is per-frame.
/// </summary>
public sealed record ChildBinding(string Name, SKShader Shader);
