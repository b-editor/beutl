using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Meshes;

namespace Beutl.Graphics3D.Nodes;

/// <summary>
/// The one-sided and double-sided pipelines a shadow pass draws casters with. The pass that creates them disposes them.
/// </summary>
internal sealed class ShadowCasterPipelines : IDisposable
{
    private const int DoubleSidedVariant = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct PushConstants
    {
        public Matrix4x4 Model;
        public Matrix4x4 LightViewProjection;
    }

    private readonly IPipeline3D?[] _cutoutPipelines = new IPipeline3D?[2];
    private readonly MaterialDrawBindingPool?[] _cutoutBindings = new MaterialDrawBindingPool?[2];
    private ISampler? _cutoutSampler;
    private IPipeline3D? _boundPipeline;

    public IPipeline3D? OneSided { get; private set; }

    public IPipeline3D? DoubleSided { get; private set; }

    [MemberNotNullWhen(true, nameof(OneSided), nameof(DoubleSided))]
    public bool IsCreated => OneSided != null && DoubleSided != null;

    public void Create(
        IGraphicsContext context,
        IRenderPass3D renderPass,
        byte[] vertexShaderSpirv,
        byte[] fragmentShaderSpirv,
        DescriptorBinding[] descriptorBindings)
    {
        OneSided?.Dispose();
        DoubleSided?.Dispose();

        var options = new PipelineOptions
        {
            DepthTestEnabled = true,
            DepthWriteEnabled = true,
            // Casters cull the sides their material culls when drawn, so a one-sided surface seen from behind
            // casts nothing; double-sided materials use the no-cull variant.
            CullMode = CullMode.Back
        };

        OneSided = context.CreatePipeline3D(
            renderPass,
            vertexShaderSpirv,
            fragmentShaderSpirv,
            descriptorBindings,
            Vertex3D.GetVertexInputDescription(),
            options);

        options.CullMode = CullMode.None;
        DoubleSided = context.CreatePipeline3D(
            renderPass,
            vertexShaderSpirv,
            fragmentShaderSpirv,
            descriptorBindings,
            Vertex3D.GetVertexInputDescription(),
            options);
    }

    /// <summary>
    /// Creates the pipelines for casters with holes in them, whose fragment shader reads a uniform buffer of
    /// <typeparamref name="TUbo"/> at binding 0 and the color map at binding 1. Most scenes have no such caster.
    /// </summary>
    public void EnsureCutoutPipelines<TUbo>(
        IGraphicsContext context,
        IRenderPass3D renderPass,
        IShaderCompiler shaderCompiler,
        string vertexShader,
        string fragmentShader)
        where TUbo : struct
    {
        if (_cutoutSampler != null)
            return;

        byte[] vertexSpirv = shaderCompiler.CompileToSpirv(vertexShader, ShaderStage.Vertex);
        byte[] fragmentSpirv = shaderCompiler.CompileToSpirv(fragmentShader, ShaderStage.Fragment);
        DescriptorBinding[] descriptorBindings = MaterialGpuResources.CreateDescriptorBindings(textureCount: 1);
        for (int variant = 0; variant < _cutoutPipelines.Length; variant++)
        {
            var options = new PipelineOptions
            {
                DepthTestEnabled = true,
                DepthWriteEnabled = true,
                CullMode = variant == DoubleSidedVariant ? CullMode.None : CullMode.Back,
            };
            IPipeline3D pipeline = context.CreatePipeline3D(
                renderPass,
                vertexSpirv,
                fragmentSpirv,
                descriptorBindings,
                Vertex3D.GetVertexInputDescription(),
                options);
            _cutoutPipelines[variant] = pipeline;
            _cutoutBindings[variant] = MaterialDrawBindingPool.Create<TUbo>(context, pipeline, 1);
        }

        _cutoutSampler = context.CreateSampler(
            SamplerFilter.Linear,
            SamplerFilter.Linear,
            SamplerAddressMode.ClampToEdge,
            SamplerAddressMode.ClampToEdge);
    }

    /// <summary>Binds the pipeline and the bindings for a caster with holes in it.</summary>
    public void BindCutout<TUbo>(IRenderPass3D renderPass, bool doubleSided, ITexture2D map, in TUbo ubo)
        where TUbo : unmanaged
    {
        int variant = doubleSided ? DoubleSidedVariant : 0;
        IPipeline3D pipeline = _cutoutPipelines[variant]!;
        MaterialDrawBindingPool pool = _cutoutBindings[variant]!;
        // Each draw has its own: a draw reads its bindings when the device executes it.
        MaterialDrawBindings bindings = pool.Acquire();
        bindings.Descriptors.UpdateTexture(1, map, _cutoutSampler!);
        pool.UploadAndBind(bindings, in ubo, renderPass, pipeline);
        _boundPipeline = pipeline;
    }

    public void Dispose()
    {
        RenderNode3D.DisposeResources(
        [
            OneSided, DoubleSided,
            _cutoutBindings[0], _cutoutBindings[1], _cutoutPipelines[0], _cutoutPipelines[1], _cutoutSampler,
        ]);
    }

    /// <summary>Forgets the bound pipeline when a new render pass instance begins.</summary>
    public void ResetBinding()
    {
        _boundPipeline = null;
    }

    /// <summary>Binds the pipeline for a caster unless it is already bound.</summary>
    /// <returns>Whether <paramref name="pipeline"/> was bound by this call.</returns>
    public bool Bind(IRenderPass3D renderPass, bool doubleSided, out IPipeline3D pipeline)
    {
        pipeline = doubleSided ? DoubleSided! : OneSided!;
        if (ReferenceEquals(pipeline, _boundPipeline))
            return false;

        renderPass.BindPipeline(pipeline);
        _boundPipeline = pipeline;
        return true;
    }

    public static void SetPushConstants(IRenderPass3D renderPass, Matrix4x4 model, Matrix4x4 lightViewProjection)
    {
        renderPass.SetPushConstants(new PushConstants
        {
            Model = model,
            LightViewProjection = lightViewProjection
        });
    }
}
