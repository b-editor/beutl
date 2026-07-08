using SkiaSharp;

namespace Beutl.Graphics.Effects;

/// <summary>
/// An extra texture sampler bound into a <see cref="ShaderNodeDescriptor"/> (e.g. a LUT), feature 004
/// data-model §1. The sampler's <em>name</em> is structural; the texture <em>contents</em> are a parameter
/// (a LUT swap re-binds without recompiling, A4). Held as an <see cref="SKShader"/> the fused executor wires as a
/// child shader.
/// </summary>
/// <remarks>
/// There are two deliberate ownership modes; pick by who disposes the shader:
/// <list type="bullet">
/// <item><description>
/// <b>Graph-scoped</b> — build it with <see cref="EffectGraphBuilder.Sampler(string, SKShader)"/> from inside
/// <c>FilterEffect.Describe</c>. The graph owns the shader and disposes it once the frame's plan has executed
/// (even if the pass is skipped for an empty ROI), so you MUST NOT dispose it yourself. Use this for a shader you
/// build fresh in <c>Describe</c> every frame — the normal case, and what the in-tree effects do.
/// </description></item>
/// <item><description>
/// <b>Caller-owned</b> — construct this record directly (<c>new SamplerBinding(name, shader)</c>). YOU keep
/// ownership and dispose the shader yourself (e.g. when your cross-frame cache evicts it); the graph will not free
/// it. Use this for a shader you cache and reuse across frames. This is a supported mode, not a leak.
/// </description></item>
/// </list>
/// </remarks>
public sealed record SamplerBinding(string Name, SKShader Shader);

/// <summary>
/// A child shader bound into a whole-source <see cref="ShaderNodeDescriptor"/> beyond the implicit <c>src</c>
/// input (feature 004, data-model §1). The name is structural; the shader instance is per-frame.
/// </summary>
/// <remarks>
/// There are two deliberate ownership modes; pick by who disposes the shader:
/// <list type="bullet">
/// <item><description>
/// <b>Graph-scoped</b> — build it with <see cref="EffectGraphBuilder.Child(string, SKShader)"/> from inside
/// <c>FilterEffect.Describe</c>. The graph owns the shader and disposes it once the frame's plan has executed
/// (even if the pass is skipped for an empty ROI), so you MUST NOT dispose it yourself. Use this for a shader you
/// build fresh in <c>Describe</c> every frame — the normal case, and what the in-tree effects do.
/// </description></item>
/// <item><description>
/// <b>Caller-owned</b> — construct this record directly (<c>new ChildBinding(name, shader)</c>). YOU keep
/// ownership and dispose the shader yourself (e.g. when your cross-frame cache evicts it); the graph will not free
/// it. Use this for a shader you cache and reuse across frames. This is a supported mode, not a leak.
/// </description></item>
/// </list>
/// </remarks>
public sealed record ChildBinding(string Name, SKShader Shader);
