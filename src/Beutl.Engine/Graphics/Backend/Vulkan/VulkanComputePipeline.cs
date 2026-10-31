using System.Collections.Immutable;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

internal sealed unsafe class VulkanComputePipeline : IDisposable
{
    private readonly VulkanContext _context;
    private readonly PipelineLayout _layout;
    private readonly DescriptorSetLayout _descriptorLayout;
    private ShaderModule _shader;
    private bool _disposed;

    public Pipeline Handle { get; private set; }
    public PipelineLayout Layout => _layout;
    public DescriptorSetLayout DescriptorLayout => _descriptorLayout;
    public VulkanDescriptorBindingTable Bindings { get; }

    public VulkanComputePipeline(VulkanContext context, byte[] source, DescriptorBinding[] bindings,
        ImmutableArray<SpecializationConstant> specializationConstants = default)
    {
        _context = context;
        DescriptorSetLayoutBinding[] declarations = bindings.Select(VulkanFlagConverter.ToVulkan).ToArray();
        Bindings = new(declarations);
        try
        {
            _shader = VulkanPipeline3D.CreateShaderModule(context.Vk, context.Device, source);
            VulkanPipeline3D.CreateLayouts(context.Vk, context.Device, declarations, ShaderStageFlags.ComputeBit,
                out _descriptorLayout, out _layout);
            var specialization = new VulkanSpecializationData(specializationConstants);
            fixed (byte* entryPoint = "main\0"u8)
            fixed (SpecializationMapEntry* entries = specialization.MapEntries)
            fixed (byte* data = specialization.Data)
            {
                SpecializationInfo values = specialization.Describe(ShaderStage.Compute, entries, data);
                var pipelineInfo = new ComputePipelineCreateInfo
                {
                    SType = StructureType.ComputePipelineCreateInfo,
                    Layout = Layout,
                    Stage = new PipelineShaderStageCreateInfo
                    {
                        SType = StructureType.PipelineShaderStageCreateInfo,
                        Stage = ShaderStageFlags.ComputeBit,
                        Module = _shader,
                        PName = entryPoint,
                        PSpecializationInfo = values.MapEntryCount == 0 ? null : &values,
                    },
                };
                Result result = context.Vk.CreateComputePipelines(context.Device, default, 1, &pipelineInfo, null, out var pipeline);
                Handle = pipeline;
                Check(result);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private static void Check(Result result)
    {
        if (result != Result.Success)
            throw new InvalidOperationException($"Failed to create compute pipeline: {result}.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Pipeline pipeline = Handle;
        PipelineLayout layout = Layout;
        DescriptorSetLayout descriptorLayout = DescriptorLayout;
        ShaderModule shader = _shader;
        _context.DeferRelease(() =>
        {
            if (pipeline.Handle != 0)
                _context.Vk.DestroyPipeline(_context.Device, pipeline, null);
            if (layout.Handle != 0)
                _context.Vk.DestroyPipelineLayout(_context.Device, layout, null);
            if (descriptorLayout.Handle != 0)
                _context.Vk.DestroyDescriptorSetLayout(_context.Device, descriptorLayout, null);
            if (shader.Handle != 0)
                _context.Vk.DestroyShaderModule(_context.Device, shader, null);
        });
    }
}
