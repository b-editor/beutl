using System;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Vulkan implementation of <see cref="IPipeline3D"/>.
/// </summary>
internal sealed unsafe class VulkanPipeline3D : IPipeline3D, IVulkanContextResource
{
    private readonly VulkanContext _context;
    private readonly RenderPass _compatibleRenderPass;
    private readonly Pipeline _pipeline;
    private readonly PipelineLayout _pipelineLayout;
    private readonly DescriptorSetLayout _descriptorSetLayout;
    private readonly VulkanDescriptorBindingTable _descriptorBindings;
    private readonly ShaderModule _vertexShader;
    private readonly ShaderModule _fragmentShader;
    private bool _disposed;

    public VulkanContext OwnerContext => _context;

    public VulkanPipeline3D(
        VulkanContext context,
        RenderPass renderPass,
        byte[] vertexShaderSpirv,
        byte[] fragmentShaderSpirv,
        VulkanVertexInputDescription vertexInputDescription,
        DescriptorSetLayoutBinding[] descriptorBindings,
        ImmutableArray<SpecializationConstant> specializationConstants,
        int colorAttachmentCount,
        bool hasDepthAttachment,
        in VulkanPipelineFixedFunctionState fixedFunctionState)
    {
        _context = context;
        _compatibleRenderPass = renderPass;
        _descriptorBindings = new VulkanDescriptorBindingTable(descriptorBindings);
        var vk = context.Vk;
        var device = context.Device;

        // A constructor that throws leaves no instance for anyone to dispose, so every device object made
        // before the failure has to go back here or it survives until the context does. The whole sequence
        // is inside the try because any step of it can fail: a rejected pipeline is the last one, but the
        // shader modules and the two layouts are already on the device by then.
        try
        {
            _vertexShader = CreateShaderModule(vk, device, vertexShaderSpirv);
            _fragmentShader = CreateShaderModule(vk, device, fragmentShaderSpirv);

            fixed (DescriptorSetLayoutBinding* bindingsPtr = descriptorBindings)
            {
                var layoutInfo = new DescriptorSetLayoutCreateInfo
                {
                    SType = StructureType.DescriptorSetLayoutCreateInfo,
                    BindingCount = (uint)descriptorBindings.Length,
                    PBindings = bindingsPtr
                };

                DescriptorSetLayout descriptorLayout;
                var result = vk.CreateDescriptorSetLayout(device, &layoutInfo, null, &descriptorLayout);
                if (result != Result.Success)
                {
                    throw new InvalidOperationException($"Failed to create descriptor set layout: {result}");
                }
                _descriptorSetLayout = descriptorLayout;
            }

            // One range covering both stages over the 128 bytes Vulkan guarantees. Because the range spans
            // both stages, every vkCmdPushConstants against this layout must name both: the spec requires
            // the update to cover all stages of every range it overlaps.
            var pushConstantRange = new PushConstantRange
            {
                StageFlags = PushConstantStages,
                Offset = 0,
                Size = MaxPushConstantsSize
            };

            var layouts = stackalloc DescriptorSetLayout[] { _descriptorSetLayout };
            var pipelineLayoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = layouts,
                PushConstantRangeCount = 1,
                PPushConstantRanges = &pushConstantRange
            };

            PipelineLayout pipelineLayout;
            var layoutResult = vk.CreatePipelineLayout(device, &pipelineLayoutInfo, null, &pipelineLayout);
            if (layoutResult != Result.Success)
            {
                throw new InvalidOperationException($"Failed to create pipeline layout: {layoutResult}");
            }
            _pipelineLayout = pipelineLayout;

            _pipeline = CreateGraphicsPipeline(
                vk, device, renderPass, vertexInputDescription, colorAttachmentCount,
                hasDepthAttachment, in fixedFunctionState, specializationConstants);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>The stages the pipeline layout's push-constant range covers.</summary>
    /// <remarks>
    /// Every <c>vkCmdPushConstants</c> against this layout must pass exactly these: the spec requires an
    /// update to name all stages of every range it overlaps, and this layout declares one range spanning
    /// them. Reading it from here rather than from the caller is what keeps the two in step.
    /// </remarks>
    public const ShaderStageFlags PushConstantStages =
        ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit;

    /// <summary>The size of the pipeline layout's push-constant range, in bytes.</summary>
    /// <remarks>128 is the minimum every Vulkan implementation guarantees.</remarks>
    public const uint MaxPushConstantsSize = 128;

    public Pipeline Handle => _pipeline;

    public PipelineLayout PipelineLayoutHandle => _pipelineLayout;

    public DescriptorSetLayout DescriptorSetLayoutHandle => _descriptorSetLayout;

    /// <summary>The bindings <see cref="DescriptorSetLayoutHandle"/> was created from.</summary>
    /// <remarks>
    /// The handle alone says nothing about what it declares, so it has to travel with these for a
    /// descriptor write against a set allocated from it to be checkable at all.
    /// </remarks>
    public VulkanDescriptorBindingTable DescriptorBindings => _descriptorBindings;

    /// <summary>Whether this pipeline was created for <paramref name="renderPass"/>.</summary>
    /// <remarks>
    /// The owning context is compared before the handle: two contexts allocate handles independently, so an
    /// equal handle value from a foreign device says nothing about compatibility.
    /// </remarks>
    public bool IsCompatibleWith(VulkanRenderPass3D renderPass)
    {
        ArgumentNullException.ThrowIfNull(renderPass);
        return ReferenceEquals(_context, renderPass.OwnerContext)
               && _compatibleRenderPass.Handle == renderPass.Handle.Handle;
    }

    private Pipeline CreateGraphicsPipeline(
        Vk vk, Device device, RenderPass renderPass, VulkanVertexInputDescription vertexInput,
        int colorAttachmentCount, bool hasDepthAttachment, in VulkanPipelineFixedFunctionState state,
        ImmutableArray<SpecializationConstant> specializationConstants)
    {
        VulkanSpecializationData vertexSpecialization = CreateSpecializationData(
            specializationConstants,
            ShaderStage.Vertex);
        VulkanSpecializationData fragmentSpecialization = CreateSpecializationData(
            specializationConstants,
            ShaderStage.Fragment);
        var mainBytes = System.Text.Encoding.UTF8.GetBytes("main\0");
        fixed (byte* mainPtr = mainBytes)
        fixed (SpecializationMapEntry* vertexEntriesPtr = vertexSpecialization.MapEntries)
        fixed (byte* vertexDataPtr = vertexSpecialization.Data)
        fixed (SpecializationMapEntry* fragmentEntriesPtr = fragmentSpecialization.MapEntries)
        fixed (byte* fragmentDataPtr = fragmentSpecialization.Data)
        {
            var vertexSpecializationInfo = DescribeSpecialization(vertexSpecialization, vertexEntriesPtr, vertexDataPtr);
            var fragmentSpecializationInfo = DescribeSpecialization(
                fragmentSpecialization, fragmentEntriesPtr, fragmentDataPtr);
            var shaderStages = stackalloc PipelineShaderStageCreateInfo[2];
            shaderStages[0] = DescribeShaderStage(
                ShaderStageFlags.VertexBit,
                _vertexShader,
                mainPtr,
                vertexSpecialization.MapEntries.Length == 0 ? null : &vertexSpecializationInfo);
            shaderStages[1] = DescribeShaderStage(
                ShaderStageFlags.FragmentBit,
                _fragmentShader,
                mainPtr,
                fragmentSpecialization.MapEntries.Length == 0 ? null : &fragmentSpecializationInfo);

            // Vertex input state
            fixed (VertexInputBindingDescription* bindingsPtr = vertexInput.Bindings)
            fixed (VertexInputAttributeDescription* attributesPtr = vertexInput.Attributes)
            {
                var vertexInputInfo = new PipelineVertexInputStateCreateInfo
                {
                    SType = StructureType.PipelineVertexInputStateCreateInfo,
                    VertexBindingDescriptionCount = (uint)vertexInput.Bindings.Length,
                    PVertexBindingDescriptions = bindingsPtr,
                    VertexAttributeDescriptionCount = (uint)vertexInput.Attributes.Length,
                    PVertexAttributeDescriptions = attributesPtr
                };

                var inputAssembly = new PipelineInputAssemblyStateCreateInfo
                {
                    SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                    Topology = PrimitiveTopology.TriangleList,
                    PrimitiveRestartEnable = Vk.False
                };

                var viewportState = new PipelineViewportStateCreateInfo
                {
                    SType = StructureType.PipelineViewportStateCreateInfo,
                    ViewportCount = 1,
                    ScissorCount = 1
                };

                var rasterizer = DescribeRasterization(in state);

                var multisampling = new PipelineMultisampleStateCreateInfo
                {
                    SType = StructureType.PipelineMultisampleStateCreateInfo,
                    SampleShadingEnable = Vk.False,
                    RasterizationSamples = SampleCountFlags.Count1Bit
                };

                var depthStencil = DescribeDepthStencil(in state);

                // Create color blend attachments for each color attachment
                var colorBlendAttachments = stackalloc PipelineColorBlendAttachmentState[colorAttachmentCount];
                for (int i = 0; i < colorAttachmentCount; i++)
                {
                    colorBlendAttachments[i] = DescribeColorBlendAttachment(in state);
                }

                var colorBlending = new PipelineColorBlendStateCreateInfo
                {
                    SType = StructureType.PipelineColorBlendStateCreateInfo,
                    LogicOpEnable = Vk.False,
                    AttachmentCount = (uint)colorAttachmentCount,
                    PAttachments = colorBlendAttachments
                };

                var dynamicStates = stackalloc DynamicState[] { DynamicState.Viewport, DynamicState.Scissor };
                var dynamicState = new PipelineDynamicStateCreateInfo
                {
                    SType = StructureType.PipelineDynamicStateCreateInfo,
                    DynamicStateCount = 2,
                    PDynamicStates = dynamicStates
                };

                var pipelineInfo = new GraphicsPipelineCreateInfo
                {
                    SType = StructureType.GraphicsPipelineCreateInfo,
                    StageCount = 2,
                    PStages = shaderStages,
                    PVertexInputState = &vertexInputInfo,
                    PInputAssemblyState = &inputAssembly,
                    PViewportState = &viewportState,
                    PRasterizationState = &rasterizer,
                    PMultisampleState = &multisampling,
                    PDepthStencilState = hasDepthAttachment ? &depthStencil : null,
                    PColorBlendState = &colorBlending,
                    PDynamicState = &dynamicState,
                    Layout = _pipelineLayout,
                    RenderPass = renderPass,
                    Subpass = 0
                };

                Pipeline pipeline;
                var result = vk.CreateGraphicsPipelines(device, default, 1, &pipelineInfo, null, &pipeline);
                if (result != Result.Success)
                {
                    throw new InvalidOperationException($"Failed to create graphics pipeline: {result}");
                }
                return pipeline;
            }
        }
    }

    // The Describe* builders only assemble structs; every pointer they store arrives as an argument that the
    // caller keeps pinned until vkCreateGraphicsPipelines returns.
    private static SpecializationInfo DescribeSpecialization(
        VulkanSpecializationData data,
        SpecializationMapEntry* entries,
        byte* bytes)
        => new()
        {
            MapEntryCount = (uint)data.MapEntries.Length,
            PMapEntries = entries,
            DataSize = (nuint)data.Data.Length,
            PData = bytes,
        };

    private static PipelineShaderStageCreateInfo DescribeShaderStage(
        ShaderStageFlags stage,
        ShaderModule module,
        byte* entryPoint,
        SpecializationInfo* specialization)
        => new()
        {
            SType = StructureType.PipelineShaderStageCreateInfo,
            Stage = stage,
            Module = module,
            PName = entryPoint,
            PSpecializationInfo = specialization,
        };

    private static PipelineRasterizationStateCreateInfo DescribeRasterization(
        in VulkanPipelineFixedFunctionState state)
        => new()
        {
            SType = StructureType.PipelineRasterizationStateCreateInfo,
            DepthClampEnable = Vk.False,
            RasterizerDiscardEnable = Vk.False,
            PolygonMode = PolygonMode.Fill,
            LineWidth = 1.0f,
            CullMode = state.CullMode,
            FrontFace = state.FrontFace,
            DepthBiasEnable = Vk.False
        };

    private static PipelineDepthStencilStateCreateInfo DescribeDepthStencil(in VulkanPipelineFixedFunctionState state)
        => new()
        {
            SType = StructureType.PipelineDepthStencilStateCreateInfo,
            DepthTestEnable = state.DepthTestEnabled ? Vk.True : Vk.False,
            DepthWriteEnable = state.DepthWriteEnabled ? Vk.True : Vk.False,
            DepthCompareOp = CompareOp.Less,
            DepthBoundsTestEnable = Vk.False,
            StencilTestEnable = Vk.False
        };

    private static PipelineColorBlendAttachmentState DescribeColorBlendAttachment(
        in VulkanPipelineFixedFunctionState state)
        => new()
        {
            ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                             ColorComponentFlags.BBit | ColorComponentFlags.ABit,
            BlendEnable = state.BlendEnabled ? Vk.True : Vk.False,
            SrcColorBlendFactor = state.SrcColorBlendFactor,
            DstColorBlendFactor = state.DstColorBlendFactor,
            ColorBlendOp = state.ColorBlendOp,
            SrcAlphaBlendFactor = state.SrcAlphaBlendFactor,
            DstAlphaBlendFactor = state.DstAlphaBlendFactor,
            AlphaBlendOp = state.AlphaBlendOp
        };

    private static VulkanSpecializationData CreateSpecializationData(
        ImmutableArray<SpecializationConstant> constants,
        ShaderStage stage)
    {
        SpecializationConstant[] stageConstants = constants
            .Where(constant => (constant.Stages & stage) == stage)
            .OrderBy(constant => constant.ConstantId)
            .ToArray();
        var entries = new SpecializationMapEntry[stageConstants.Length];
        var data = new byte[stageConstants.Sum(constant => constant.SizeInBytes)];
        int offset = 0;

        for (int i = 0; i < stageConstants.Length; i++)
        {
            SpecializationConstant constant = stageConstants[i];
            entries[i] = new SpecializationMapEntry
            {
                ConstantID = constant.ConstantId,
                Offset = (uint)offset,
                Size = (nuint)constant.SizeInBytes,
            };
            constant.CopyValueTo(data.AsSpan(offset, constant.SizeInBytes));
            offset += constant.SizeInBytes;
        }

        return new VulkanSpecializationData(entries, data);
    }

    private sealed record VulkanSpecializationData(
        SpecializationMapEntry[] MapEntries,
        byte[] Data);

    internal static ShaderModule CreateShaderModule(Vk vk, Device device, byte[] spirv)
    {
        fixed (byte* codePtr = spirv)
        {
            var createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)spirv.Length,
                PCode = (uint*)codePtr
            };

            ShaderModule module;
            var result = vk.CreateShaderModule(device, &createInfo, null, &module);
            if (result != Result.Success)
            {
                throw new InvalidOperationException($"Failed to create shader module: {result}");
            }
            return module;
        }
    }

    public void Bind()
    {
        // Binding is done through command buffer
        // This method is kept for interface compatibility
    }

    /// <remarks>
    /// Also the release path for a constructor that threw, so every handle is checked before it is
    /// destroyed: a partially built pipeline leaves the later ones at their default, unset value.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Pipeline pipeline = _pipeline;
        PipelineLayout pipelineLayout = _pipelineLayout;
        DescriptorSetLayout descriptorSetLayout = _descriptorSetLayout;
        ShaderModule vertexShader = _vertexShader;
        ShaderModule fragmentShader = _fragmentShader;
        _context.DeferRelease(() =>
        {
            var vk = _context.Vk;
            var device = _context.Device;

            if (pipeline.Handle != 0)
                vk.DestroyPipeline(device, pipeline, null);

            if (pipelineLayout.Handle != 0)
                vk.DestroyPipelineLayout(device, pipelineLayout, null);

            if (descriptorSetLayout.Handle != 0)
                vk.DestroyDescriptorSetLayout(device, descriptorSetLayout, null);

            if (vertexShader.Handle != 0)
                vk.DestroyShaderModule(device, vertexShader, null);
            if (fragmentShader.Handle != 0)
                vk.DestroyShaderModule(device, fragmentShader, null);
        });
    }
}
