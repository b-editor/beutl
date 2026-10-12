using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D.Meshes;

namespace Beutl.Graphics3D.Nodes;

/// <summary>
/// The one-sided and double-sided pipelines a shadow pass draws casters with. The pass that creates them disposes them.
/// </summary>
internal sealed class ShadowCasterPipelines
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PushConstants
    {
        public Matrix4x4 Model;
        public Matrix4x4 LightViewProjection;
    }

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
