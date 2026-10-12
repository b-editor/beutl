using System.Diagnostics.CodeAnalysis;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Meshes;

namespace Beutl.Graphics3D.Materials;

/// <summary>
/// The pipeline a built-in material draws with, built for one render pass, with the sampler, the 1x1 fallback
/// textures and the per-draw bindings that go with it.
/// </summary>
internal sealed class MaterialPipelineState
{
    private IRenderPass3D? _renderPass;
    private ITexture2D?[] _fallbackTextures = [];

    public IPipeline3D? Pipeline { get; private set; }

    public MaterialDrawBindingPool? DrawBindings { get; private set; }

    public ISampler? Sampler { get; private set; }

    [MemberNotNullWhen(true, nameof(Pipeline), nameof(DrawBindings), nameof(Sampler))]
    public bool IsBuilt => Pipeline != null && DrawBindings != null && Sampler != null;

    public bool IsBuiltFor(IRenderPass3D renderPass) => ReferenceEquals(_renderPass, renderPass);

    /// <param name="fallbackTextures">The BGRA pixel of each fallback texture, in the order <see cref="UpdateTexture"/> indexes them.</param>
    public void Build<TUbo>(
        RenderContext3D context,
        string vertexShaderSource,
        string fragmentShaderSource,
        uint textureCount,
        PipelineOptions? options,
        ReadOnlySpan<byte[]> fallbackTextures)
        where TUbo : struct
    {
        var graphicsContext = context.GraphicsContext;
        var shaderCompiler = context.ShaderCompiler;

        Sampler = MaterialGpuResources.CreateLinearRepeatSampler(graphicsContext);
        _fallbackTextures = new ITexture2D?[fallbackTextures.Length];
        for (int i = 0; i < fallbackTextures.Length; i++)
        {
            _fallbackTextures[i] = MaterialGpuResources.Create1x1Texture(graphicsContext, fallbackTextures[i]);
        }

        var vertexSpirv = shaderCompiler.CompileToSpirv(vertexShaderSource, ShaderStage.Vertex);
        var fragmentSpirv = shaderCompiler.CompileToSpirv(fragmentShaderSource, ShaderStage.Fragment);

        Pipeline = graphicsContext.CreatePipeline3D(
            context.RenderPass,
            vertexSpirv,
            fragmentSpirv,
            MaterialGpuResources.CreateDescriptorBindings(textureCount),
            Vertex3D.GetVertexInputDescription(),
            options);

        DrawBindings = MaterialDrawBindingPool.Create<TUbo>(graphicsContext, Pipeline, textureCount);

        _renderPass = context.RenderPass;
    }

    /// <summary>
    /// Binds <paramref name="texture"/> at <paramref name="binding"/>, or the fallback texture at
    /// <paramref name="fallbackTexture"/> when there is none.
    /// </summary>
    public void UpdateTexture(MaterialDrawBindings bindings, int binding, ITexture2D? texture, int fallbackTexture = 0)
    {
        bindings.Descriptors.UpdateTexture(binding, texture ?? _fallbackTextures[fallbackTexture]!, Sampler!);
    }

    public void Dispose()
    {
        _renderPass = null;
        DrawBindings?.Dispose();
        DrawBindings = null;
        Sampler?.Dispose();
        Sampler = null;

        foreach (ITexture2D? texture in _fallbackTextures)
        {
            texture?.Dispose();
        }
        _fallbackTextures = [];

        Pipeline?.Dispose();
        Pipeline = null;
    }
}
