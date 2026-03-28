using System.Runtime.InteropServices;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using VkAttachmentLoadOp = Silk.NET.Vulkan.AttachmentLoadOp;
using VkBlendFactor = Silk.NET.Vulkan.BlendFactor;
using VkBlendOp = Silk.NET.Vulkan.BlendOp;
using VkDescriptorPoolSize = Silk.NET.Vulkan.DescriptorPoolSize;
using VkDescriptorType = Silk.NET.Vulkan.DescriptorType;
using VkFrontFace = Silk.NET.Vulkan.FrontFace;
using VkSamplerAddressMode = Silk.NET.Vulkan.SamplerAddressMode;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Push constants for the present pipeline.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PresentPushConstants
{
    public float SrcX;
    public float SrcY;
    public float SrcW;
    public float SrcH;
    public float DstX;
    public float DstY;
    public float DstW;
    public float DstH;
    public float Exposure;
    public int TmOperator; // 0=None, 1=Reinhard, 2=ACES, 3=Hable
    public int IsHdr;      // 1 = HDR swapchain, 0 = SDR
    private int _padding;
}

/// <summary>
/// Fullscreen quad pipeline for presenting bitmaps to a swapchain with tone mapping support.
/// </summary>
internal sealed unsafe class VulkanPresentPipeline : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<VulkanPresentPipeline>();

    private const string VertexShaderSource = """
        #version 450
        layout(location = 0) out vec2 uv;
        void main() {
            uv = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
            gl_Position = vec4(uv * 2.0 - 1.0, 0.0, 1.0);
            uv.y = 1.0 - uv.y;
        }
        """;

    private const string FragmentShaderSource = """
        #version 450
        layout(location = 0) in vec2 uv;
        layout(location = 0) out vec4 outColor;

        layout(set = 0, binding = 0) uniform sampler2D srcTexture;

        layout(push_constant) uniform PushConstants {
            vec4 srcRect;
            vec4 dstRect;
            float exposure;
            int tmOperator;
            int isHdr;
        } pc;

        vec3 reinhard(vec3 c) { return c / (1.0 + c); }

        vec3 aces(vec3 c) {
            float a = 2.51, b = 0.03, cc = 2.43, d = 0.59, e = 0.14;
            return clamp((c * (a * c + b)) / (c * (cc * c + d) + e), 0.0, 1.0);
        }

        vec3 hable_partial(vec3 x) {
            float A = 0.15, B = 0.50, C = 0.10, D = 0.20, E = 0.02, F = 0.30;
            return ((x * (A * x + C * B) + D * E) / (x * (A * x + B) + D * F)) - E / F;
        }

        vec3 hable(vec3 c) {
            return hable_partial(c) / hable_partial(vec3(11.2));
        }

        vec3 linearToSrgb(vec3 c) {
            vec3 lo = c * 12.92;
            vec3 hi = 1.055 * pow(max(c, vec3(0.0)), vec3(1.0 / 2.4)) - 0.055;
            return mix(lo, hi, step(vec3(0.0031308), c));
        }

        void main() {
            // Map UV through stretch viewport
            vec2 srcUV = (uv - pc.dstRect.xy) / pc.dstRect.zw * pc.srcRect.zw + pc.srcRect.xy;

            if (srcUV.x < 0.0 || srcUV.x > 1.0 || srcUV.y < 0.0 || srcUV.y > 1.0) {
                outColor = vec4(0.0);
                return;
            }

            vec4 c = texture(srcTexture, vec2(srcUV.x, pc.srcRect.z - srcUV.y));
            float alpha = c.a;
            if (alpha <= 0.0001) { outColor = vec4(0.0); return; }

            vec3 rgb = c.rgb / alpha;
            rgb *= exp2(pc.exposure);

            if (pc.tmOperator == 1) rgb = reinhard(max(rgb, vec3(0.0)));
            else if (pc.tmOperator == 2) rgb = aces(max(rgb, vec3(0.0)));
            else if (pc.tmOperator == 3) rgb = hable(max(rgb, vec3(0.0)));

            if (pc.isHdr == 0) {
                rgb = clamp(rgb, 0.0, 1.0);
                rgb = linearToSrgb(rgb);
            }

            outColor = vec4(rgb * alpha, alpha);
        }
        """;

    private readonly Vk _vk;
    private readonly Device _device;

    private RenderPass _renderPass;
    private PipelineLayout _pipelineLayout;
    private Pipeline _pipeline;
    private DescriptorSetLayout _descriptorSetLayout;
    private DescriptorPool _descriptorPool;
    private Silk.NET.Vulkan.Sampler _sampler;
    private Framebuffer[] _framebuffers = [];
    private bool _disposed;

    public VulkanPresentPipeline(Vk vk, Device device, Format swapchainFormat, ImageView[] swapchainImageViews, Extent2D extent)
    {
        _vk = vk;
        _device = device;

        CreateRenderPass(swapchainFormat);
        CreateDescriptorSetLayout();
        CreatePipelineLayout();
        CreatePipeline(extent);
        CreateSampler();
        CreateDescriptorPool();
        CreateFramebuffers(swapchainImageViews, extent);
    }

    public RenderPass RenderPassHandle => _renderPass;
    public PipelineLayout PipelineLayoutHandle => _pipelineLayout;
    public Pipeline PipelineHandle => _pipeline;
    public DescriptorSetLayout DescriptorSetLayoutHandle => _descriptorSetLayout;
    public DescriptorPool DescriptorPoolHandle => _descriptorPool;
    public Silk.NET.Vulkan.Sampler SamplerHandle => _sampler;
    public Framebuffer[] Framebuffers => _framebuffers;

    public DescriptorSet AllocateDescriptorSet()
    {
        fixed (DescriptorSetLayout* pLayout = &_descriptorSetLayout)
        {
            var allocInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = _descriptorPool,
                DescriptorSetCount = 1,
                PSetLayouts = pLayout
            };

            DescriptorSet descriptorSet;
            var result = _vk.AllocateDescriptorSets(_device, &allocInfo, &descriptorSet);
            if (result != Result.Success)
                throw new InvalidOperationException($"Failed to allocate descriptor set: {result}");

            return descriptorSet;
        }
    }

    public void UpdateDescriptorSet(DescriptorSet descriptorSet, ImageView textureView)
    {
        var imageInfo = new DescriptorImageInfo
        {
            Sampler = _sampler,
            ImageView = textureView,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal
        };

        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = descriptorSet,
            DstBinding = 0,
            DstArrayElement = 0,
            DescriptorType = VkDescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            PImageInfo = &imageInfo
        };

        _vk.UpdateDescriptorSets(_device, 1, &write, 0, null);
    }

    public void RecreateFramebuffers(ImageView[] swapchainImageViews, Extent2D extent)
    {
        DestroyFramebuffers();
        CreateFramebuffers(swapchainImageViews, extent);
    }

    private void CreateRenderPass(Format swapchainFormat)
    {
        var colorAttachment = new AttachmentDescription
        {
            Format = swapchainFormat,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = VkAttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = VkAttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.PresentSrcKhr
        };

        var colorAttachmentRef = new AttachmentReference
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal
        };

        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorAttachmentRef
        };

        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            SrcAccessMask = 0,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit
        };

        var renderPassInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &colorAttachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency
        };

        RenderPass renderPass;
        var result = _vk.CreateRenderPass(_device, &renderPassInfo, null, &renderPass);
        if (result != Result.Success)
            throw new InvalidOperationException($"Failed to create render pass: {result}");

        _renderPass = renderPass;
    }

    private void CreateDescriptorSetLayout()
    {
        var binding = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = VkDescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
            StageFlags = ShaderStageFlags.FragmentBit
        };

        var layoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 1,
            PBindings = &binding
        };

        DescriptorSetLayout layout;
        var result = _vk.CreateDescriptorSetLayout(_device, &layoutInfo, null, &layout);
        if (result != Result.Success)
            throw new InvalidOperationException($"Failed to create descriptor set layout: {result}");

        _descriptorSetLayout = layout;
    }

    private void CreatePipelineLayout()
    {
        var pushConstantRange = new PushConstantRange
        {
            StageFlags = ShaderStageFlags.FragmentBit,
            Offset = 0,
            Size = (uint)sizeof(PresentPushConstants)
        };

        fixed (DescriptorSetLayout* pLayout = &_descriptorSetLayout)
        {
            var layoutInfo = new PipelineLayoutCreateInfo
            {
                SType = StructureType.PipelineLayoutCreateInfo,
                SetLayoutCount = 1,
                PSetLayouts = pLayout,
                PushConstantRangeCount = 1,
                PPushConstantRanges = &pushConstantRange
            };

            PipelineLayout pipelineLayout;
            var result = _vk.CreatePipelineLayout(_device, &layoutInfo, null, &pipelineLayout);
            if (result != Result.Success)
                throw new InvalidOperationException($"Failed to create pipeline layout: {result}");

            _pipelineLayout = pipelineLayout;
        }
    }

    private void CreatePipeline(Extent2D extent)
    {
        using var compiler = new VulkanShaderCompiler();
        var vertSpirv = compiler.CompileToSpirv(VertexShaderSource, ShaderStage.Vertex);
        var fragSpirv = compiler.CompileToSpirv(FragmentShaderSource, ShaderStage.Fragment);

        var vertModule = CreateShaderModule(vertSpirv);
        var fragModule = CreateShaderModule(fragSpirv);

        try
        {
            var entryPoint = (byte*)Marshal.StringToHGlobalAnsi("main");

            var shaderStages = stackalloc PipelineShaderStageCreateInfo[2];
            shaderStages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = vertModule,
                PName = entryPoint
            };
            shaderStages[1] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = fragModule,
                PName = entryPoint
            };

            var vertexInputInfo = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo
            };

            var inputAssembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList
            };

            var viewport = new Viewport
            {
                X = 0,
                Y = 0,
                Width = extent.Width,
                Height = extent.Height,
                MinDepth = 0,
                MaxDepth = 1
            };

            var scissor = new Rect2D
            {
                Offset = new Offset2D(0, 0),
                Extent = extent
            };

            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                PViewports = &viewport,
                ScissorCount = 1,
                PScissors = &scissor
            };

            var rasterizer = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                LineWidth = 1.0f,
                CullMode = CullModeFlags.None,
                FrontFace = VkFrontFace.CounterClockwise
            };

            var multisampling = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit
            };

            var colorBlendAttachment = new PipelineColorBlendAttachmentState
            {
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                 ColorComponentFlags.BBit | ColorComponentFlags.ABit,
                BlendEnable = Vk.True,
                SrcColorBlendFactor = VkBlendFactor.One,
                DstColorBlendFactor = VkBlendFactor.OneMinusSrcAlpha,
                ColorBlendOp = VkBlendOp.Add,
                SrcAlphaBlendFactor = VkBlendFactor.One,
                DstAlphaBlendFactor = VkBlendFactor.OneMinusSrcAlpha,
                AlphaBlendOp = VkBlendOp.Add
            };

            var colorBlending = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = &colorBlendAttachment
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
                PColorBlendState = &colorBlending,
                PDynamicState = &dynamicState,
                Layout = _pipelineLayout,
                RenderPass = _renderPass,
                Subpass = 0
            };

            Pipeline pipeline;
            var result = _vk.CreateGraphicsPipelines(_device, default, 1, &pipelineInfo, null, &pipeline);
            if (result != Result.Success)
                throw new InvalidOperationException($"Failed to create graphics pipeline: {result}");

            _pipeline = pipeline;

            Marshal.FreeHGlobal((IntPtr)entryPoint);
        }
        finally
        {
            _vk.DestroyShaderModule(_device, vertModule, null);
            _vk.DestroyShaderModule(_device, fragModule, null);
        }

        s_logger.LogDebug("Created present pipeline with tone mapping shaders");
    }

    private ShaderModule CreateShaderModule(byte[] spirv)
    {
        fixed (byte* pCode = spirv)
        {
            var createInfo = new ShaderModuleCreateInfo
            {
                SType = StructureType.ShaderModuleCreateInfo,
                CodeSize = (nuint)spirv.Length,
                PCode = (uint*)pCode
            };

            ShaderModule module;
            var result = _vk.CreateShaderModule(_device, &createInfo, null, &module);
            if (result != Result.Success)
                throw new InvalidOperationException($"Failed to create shader module: {result}");

            return module;
        }
    }

    private void CreateSampler()
    {
        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            AddressModeU = VkSamplerAddressMode.ClampToEdge,
            AddressModeV = VkSamplerAddressMode.ClampToEdge,
            AddressModeW = VkSamplerAddressMode.ClampToEdge,
            MipmapMode = SamplerMipmapMode.Linear,
            MaxLod = 1.0f
        };

        Silk.NET.Vulkan.Sampler sampler;
        var result = _vk.CreateSampler(_device, &samplerInfo, null, &sampler);
        if (result != Result.Success)
            throw new InvalidOperationException($"Failed to create sampler: {result}");

        _sampler = sampler;
    }

    private void CreateDescriptorPool()
    {
        var poolSize = new VkDescriptorPoolSize
        {
            Type = VkDescriptorType.CombinedImageSampler,
            DescriptorCount = 4
        };

        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            PoolSizeCount = 1,
            PPoolSizes = &poolSize,
            MaxSets = 4,
            Flags = DescriptorPoolCreateFlags.FreeDescriptorSetBit
        };

        DescriptorPool pool;
        var result = _vk.CreateDescriptorPool(_device, &poolInfo, null, &pool);
        if (result != Result.Success)
            throw new InvalidOperationException($"Failed to create descriptor pool: {result}");

        _descriptorPool = pool;
    }

    private void CreateFramebuffers(ImageView[] swapchainImageViews, Extent2D extent)
    {
        _framebuffers = new Framebuffer[swapchainImageViews.Length];

        for (int i = 0; i < swapchainImageViews.Length; i++)
        {
            var attachment = swapchainImageViews[i];

            var framebufferInfo = new FramebufferCreateInfo
            {
                SType = StructureType.FramebufferCreateInfo,
                RenderPass = _renderPass,
                AttachmentCount = 1,
                PAttachments = &attachment,
                Width = extent.Width,
                Height = extent.Height,
                Layers = 1
            };

            Framebuffer framebuffer;
            var result = _vk.CreateFramebuffer(_device, &framebufferInfo, null, &framebuffer);
            if (result != Result.Success)
                throw new InvalidOperationException($"Failed to create framebuffer: {result}");

            _framebuffers[i] = framebuffer;
        }
    }

    private void DestroyFramebuffers()
    {
        foreach (var fb in _framebuffers)
        {
            if (fb.Handle != 0)
                _vk.DestroyFramebuffer(_device, fb, null);
        }

        _framebuffers = [];
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _vk.DeviceWaitIdle(_device);

        DestroyFramebuffers();

        if (_sampler.Handle != 0)
            _vk.DestroySampler(_device, _sampler, null);

        if (_descriptorPool.Handle != 0)
            _vk.DestroyDescriptorPool(_device, _descriptorPool, null);

        if (_pipeline.Handle != 0)
            _vk.DestroyPipeline(_device, _pipeline, null);

        if (_pipelineLayout.Handle != 0)
            _vk.DestroyPipelineLayout(_device, _pipelineLayout, null);

        if (_descriptorSetLayout.Handle != 0)
            _vk.DestroyDescriptorSetLayout(_device, _descriptorSetLayout, null);

        if (_renderPass.Handle != 0)
            _vk.DestroyRenderPass(_device, _renderPass, null);
    }
}
