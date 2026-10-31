using System;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

internal sealed unsafe class VulkanPipeline3D : IPipeline3D, IVulkanContextResource
{
    private readonly VulkanContext _context;
    private readonly RenderPass _compatibleRenderPass;
    private readonly Pipeline _pipeline;
    private readonly PipelineLayout _pipelineLayout;
    private readonly DescriptorSetLayout _descriptorSetLayout;
    private readonly VulkanDescriptorBindingTable _descriptorBindings;
    private readonly Dictionary<ShaderStage, ShaderModule> _shaders = [];
    private readonly uint _patchControlPoints;
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
        : this(
            context,
            renderPass,
            new Dictionary<ShaderStage, byte[]>
            {
                [ShaderStage.Vertex] = vertexShaderSpirv,
                [ShaderStage.Fragment] = fragmentShaderSpirv,
            },
            vertexInputDescription,
            descriptorBindings,
            specializationConstants,
            colorAttachmentCount,
            hasDepthAttachment,
            in fixedFunctionState)
    {
    }

    /// <summary>Creates a pipeline from every stage of a graphics program, which includes vertex and fragment.</summary>
    public VulkanPipeline3D(
        VulkanContext context,
        RenderPass renderPass,
        IReadOnlyDictionary<ShaderStage, byte[]> stages,
        VulkanVertexInputDescription vertexInputDescription,
        DescriptorSetLayoutBinding[] descriptorBindings,
        ImmutableArray<SpecializationConstant> specializationConstants,
        int colorAttachmentCount,
        bool hasDepthAttachment,
        in VulkanPipelineFixedFunctionState fixedFunctionState,
        uint patchControlPoints = 3)
    {
        _context = context;
        _patchControlPoints = patchControlPoints;
        _compatibleRenderPass = renderPass;
        _descriptorBindings = new VulkanDescriptorBindingTable(descriptorBindings);
        var vk = context.Vk;
        var device = context.Device;

        // A constructor that throws leaves no instance to dispose, so what it already created goes back here.
        try
        {
            // The pipeline lists its stages in this order: vertex, fragment, then the optional ones.
            _shaders.Add(ShaderStage.Vertex, CreateShaderModule(vk, device, stages[ShaderStage.Vertex]));
            _shaders.Add(ShaderStage.Fragment, CreateShaderModule(vk, device, stages[ShaderStage.Fragment]));
            foreach (var (stage, bytes) in stages)
            {
                if (stage is not (ShaderStage.Vertex or ShaderStage.Fragment))
                    _shaders.Add(stage, CreateShaderModule(vk, device, bytes));
            }

            CreateLayouts(vk, device, descriptorBindings, PushConstantStages, out _descriptorSetLayout, out _pipelineLayout);

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
        ShaderStageFlags.AllGraphics;

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
        var specialization = new VulkanSpecializationData(specializationConstants);
        ShaderStage[] stages = [.. _shaders.Keys];
        fixed (byte* mainPtr = "main\0"u8)
        fixed (SpecializationMapEntry* entries = specialization.MapEntries)
        fixed (byte* data = specialization.Data)
        {
            var values = stackalloc SpecializationInfo[stages.Length];
            var shaderStages = stackalloc PipelineShaderStageCreateInfo[stages.Length];
            for (int i = 0; i < stages.Length; i++)
            {
                ShaderStage stage = stages[i];
                ShaderModule module = _shaders[stage];
                values[i] = specialization.Describe(stage, entries, data);
                shaderStages[i] = DescribeShaderStage(VulkanFlagConverter.ToVulkan(stage), module, mainPtr,
                    values[i].MapEntryCount == 0 ? null : &values[i]);
            }
            var tessellation = new PipelineTessellationStateCreateInfo
            {
                SType = StructureType.PipelineTessellationStateCreateInfo,
                PatchControlPoints = _patchControlPoints,
            };

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
                    Topology = state.Topology,
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
                    StageCount = (uint)stages.Length,
                    PTessellationState = _shaders.ContainsKey(ShaderStage.TessellationControl) ? &tessellation : null,
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
            DepthCompareOp = state.DepthCompare,
            DepthBoundsTestEnable = Vk.False,
            StencilTestEnable = state.StencilEnabled,
            Front = state.StencilFront,
            Back = state.StencilBack
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

    /// <summary>
    /// Creates a descriptor set layout for <paramref name="bindings"/> and a pipeline layout over it whose one
    /// <see cref="MaxPushConstantsSize"/>-byte push-constant range covers <paramref name="pushStages"/>.
    /// </summary>
    /// <remarks>
    /// Each handle is stored as soon as it exists, so an owner that passes its own fields still releases the
    /// descriptor set layout when the pipeline layout cannot be created.
    /// </remarks>
    internal static void CreateLayouts(Vk vk, Device device, DescriptorSetLayoutBinding[] bindings,
        ShaderStageFlags pushStages, out DescriptorSetLayout descriptorSetLayout, out PipelineLayout pipelineLayout)
    {
        fixed (DescriptorSetLayoutBinding* bindingsPtr = bindings)
        {
            var layoutInfo = new DescriptorSetLayoutCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = (uint)bindings.Length,
                PBindings = bindingsPtr
            };

            DescriptorSetLayout descriptorLayout;
            var result = vk.CreateDescriptorSetLayout(device, &layoutInfo, null, &descriptorLayout);
            if (result != Result.Success)
            {
                throw new InvalidOperationException($"Failed to create descriptor set layout: {result}");
            }
            descriptorSetLayout = descriptorLayout;
        }

        var pushConstantRange = new PushConstantRange
        {
            StageFlags = pushStages,
            Offset = 0,
            Size = MaxPushConstantsSize
        };

        var layouts = stackalloc DescriptorSetLayout[] { descriptorSetLayout };
        var pipelineLayoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = layouts,
            PushConstantRangeCount = 1,
            PPushConstantRanges = &pushConstantRange
        };

        PipelineLayout created;
        var layoutResult = vk.CreatePipelineLayout(device, &pipelineLayoutInfo, null, &created);
        if (layoutResult != Result.Success)
        {
            throw new InvalidOperationException($"Failed to create pipeline layout: {layoutResult}");
        }
        pipelineLayout = created;
    }

    public void Bind()
    {
        // Binding is done through command buffer
        // This method is kept for interface compatibility
    }

    /// <remarks>Also releases a partially built pipeline, whose later handles are still unset.</remarks>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Pipeline pipeline = _pipeline;
        PipelineLayout pipelineLayout = _pipelineLayout;
        DescriptorSetLayout descriptorSetLayout = _descriptorSetLayout;
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

            foreach (ShaderModule module in _shaders.Values)
                vk.DestroyShaderModule(device, module, null);
        });
    }
}
