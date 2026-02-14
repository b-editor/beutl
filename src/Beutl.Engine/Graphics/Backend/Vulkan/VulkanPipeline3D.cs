using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Vulkan implementation of <see cref="IPipeline3D"/>.
/// </summary>
internal sealed unsafe class VulkanPipeline3D : IPipeline3D
{
    private readonly VulkanContext _context;
    private readonly Pipeline _pipeline;
    private readonly PipelineLayout _pipelineLayout;
    private readonly DescriptorSetLayout _descriptorSetLayout;
    private readonly ShaderModule _vertexShader;
    private readonly ShaderModule _fragmentShader;
    private bool _disposed;

    public VulkanPipeline3D(
        VulkanContext context,
        RenderPass renderPass,
        byte[] vertexShaderSpirv,
        byte[] fragmentShaderSpirv,
        VulkanVertexInputDescription vertexInputDescription,
        DescriptorSetLayoutBinding[] descriptorBindings,
        int colorAttachmentCount = 1,
        bool depthTestEnabled = true,
        bool depthWriteEnabled = true,
        CullModeFlags cullMode = CullModeFlags.BackBit,
        Silk.NET.Vulkan.FrontFace frontFace = Silk.NET.Vulkan.FrontFace.CounterClockwise,
        bool blendEnabled = false,
        Silk.NET.Vulkan.BlendFactor srcColorBlendFactor = Silk.NET.Vulkan.BlendFactor.One,
        Silk.NET.Vulkan.BlendFactor dstColorBlendFactor = Silk.NET.Vulkan.BlendFactor.Zero,
        Silk.NET.Vulkan.BlendFactor srcAlphaBlendFactor = Silk.NET.Vulkan.BlendFactor.One,
        Silk.NET.Vulkan.BlendFactor dstAlphaBlendFactor = Silk.NET.Vulkan.BlendFactor.Zero,
        Silk.NET.Vulkan.BlendOp colorBlendOp = Silk.NET.Vulkan.BlendOp.Add,
        Silk.NET.Vulkan.BlendOp alphaBlendOp = Silk.NET.Vulkan.BlendOp.Add)
    {
        _context = context;
        var vk = context.Vk;
        var device = context.Device;

        // Create shader modules
        _vertexShader = CreateShaderModule(vk, device, vertexShaderSpirv);
        _fragmentShader = CreateShaderModule(vk, device, fragmentShaderSpirv);

        // Create descriptor set layout
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
                CleanupShaderModules(vk, device);
                throw new InvalidOperationException($"Failed to create descriptor set layout: {result}");
            }
            _descriptorSetLayout = descriptorLayout;
        }

        // Create pipeline layout with push constants support
        // Use 128 bytes which is the minimum guaranteed by Vulkan
        var pushConstantRange = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
            Offset = 0,
            Size = 128
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
            vk.DestroyDescriptorSetLayout(device, _descriptorSetLayout, null);
            CleanupShaderModules(vk, device);
            throw new InvalidOperationException($"Failed to create pipeline layout: {layoutResult}");
        }
        _pipelineLayout = pipelineLayout;

        // Create graphics pipeline
        _pipeline = CreateGraphicsPipeline(
            vk, device, renderPass, vertexInputDescription, colorAttachmentCount,
            depthTestEnabled, depthWriteEnabled, cullMode, frontFace,
            blendEnabled, srcColorBlendFactor, dstColorBlendFactor,
            srcAlphaBlendFactor, dstAlphaBlendFactor, colorBlendOp, alphaBlendOp);
    }

    public Pipeline Handle => _pipeline;

    public PipelineLayout PipelineLayoutHandle => _pipelineLayout;

    public DescriptorSetLayout DescriptorSetLayoutHandle => _descriptorSetLayout;

    private Pipeline CreateGraphicsPipeline(
        Vk vk, Device device, RenderPass renderPass, VulkanVertexInputDescription vertexInput,
        int colorAttachmentCount, bool depthTestEnabled, bool depthWriteEnabled,
        CullModeFlags cullMode, Silk.NET.Vulkan.FrontFace frontFace,
        bool blendEnabled, Silk.NET.Vulkan.BlendFactor srcColorBlendFactor,
        Silk.NET.Vulkan.BlendFactor dstColorBlendFactor, Silk.NET.Vulkan.BlendFactor srcAlphaBlendFactor,
        Silk.NET.Vulkan.BlendFactor dstAlphaBlendFactor, Silk.NET.Vulkan.BlendOp colorBlendOp,
        Silk.NET.Vulkan.BlendOp alphaBlendOp)
    {
        var mainBytes = System.Text.Encoding.UTF8.GetBytes("main\0");
        fixed (byte* mainPtr = mainBytes)
        {
            var shaderStages = stackalloc PipelineShaderStageCreateInfo[2];
            shaderStages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = _vertexShader,
                PName = mainPtr
            };
            shaderStages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = _fragmentShader,
                PName = mainPtr
            };

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

                var rasterizer = new PipelineRasterizationStateCreateInfo
                {
                    SType = StructureType.PipelineRasterizationStateCreateInfo,
                    DepthClampEnable = Vk.False,
                    RasterizerDiscardEnable = Vk.False,
                    PolygonMode = PolygonMode.Fill,
                    LineWidth = 1.0f,
                    CullMode = cullMode,
                    FrontFace = frontFace,
                    DepthBiasEnable = Vk.False
                };

                var multisampling = new PipelineMultisampleStateCreateInfo
                {
                    SType = StructureType.PipelineMultisampleStateCreateInfo,
                    SampleShadingEnable = Vk.False,
                    RasterizationSamples = SampleCountFlags.Count1Bit
                };

                var depthStencil = new PipelineDepthStencilStateCreateInfo
                {
                    SType = StructureType.PipelineDepthStencilStateCreateInfo,
                    DepthTestEnable = depthTestEnabled ? Vk.True : Vk.False,
                    DepthWriteEnable = depthWriteEnabled ? Vk.True : Vk.False,
                    DepthCompareOp = CompareOp.Less,
                    DepthBoundsTestEnable = Vk.False,
                    StencilTestEnable = Vk.False
                };

                // Create color blend attachments for each color attachment
                var colorBlendAttachments = stackalloc PipelineColorBlendAttachmentState[colorAttachmentCount];
                for (int i = 0; i < colorAttachmentCount; i++)
                {
                    colorBlendAttachments[i] = new PipelineColorBlendAttachmentState
                    {
                        ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                         ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                        BlendEnable = blendEnabled ? Vk.True : Vk.False,
                        SrcColorBlendFactor = srcColorBlendFactor,
                        DstColorBlendFactor = dstColorBlendFactor,
                        ColorBlendOp = colorBlendOp,
                        SrcAlphaBlendFactor = srcAlphaBlendFactor,
                        DstAlphaBlendFactor = dstAlphaBlendFactor,
                        AlphaBlendOp = alphaBlendOp
                    };
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
                    PDepthStencilState = &depthStencil,
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

    private static ShaderModule CreateShaderModule(Vk vk, Device device, byte[] spirv)
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

    private void CleanupShaderModules(Vk vk, Device device)
    {
        if (_vertexShader.Handle != 0)
            vk.DestroyShaderModule(device, _vertexShader, null);
        if (_fragmentShader.Handle != 0)
            vk.DestroyShaderModule(device, _fragmentShader, null);
    }

    public void Bind()
    {
        // Binding is done through command buffer
        // This method is kept for interface compatibility
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        var vk = _context.Vk;
        var device = _context.Device;

        if (_pipeline.Handle != 0)
            vk.DestroyPipeline(device, _pipeline, null);

        if (_pipelineLayout.Handle != 0)
            vk.DestroyPipelineLayout(device, _pipelineLayout, null);

        if (_descriptorSetLayout.Handle != 0)
            vk.DestroyDescriptorSetLayout(device, _descriptorSetLayout, null);

        CleanupShaderModules(vk, device);
    }
}

/// <summary>
/// Vulkan-specific vertex input description.
/// </summary>
internal struct VulkanVertexInputDescription
{
    public VertexInputBindingDescription[] Bindings;
    public VertexInputAttributeDescription[] Attributes;
}

