using System.Runtime.InteropServices;
using System.Text.Json;
using Beutl.Graphics3D;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using SkiaSharp;

namespace Beutl.Graphics.Backend.Vulkan;

using Image = Silk.NET.Vulkan.Image;

internal sealed class VulkanContext : IGraphicsContext
{
    private static readonly ILogger s_logger = Log.CreateLogger<VulkanContext>();
    private readonly VulkanInstance _vulkanInstance;
    private readonly VulkanDevice _vulkanDevice;
    private readonly VulkanCommandPool _vulkanCommandPool;
    private GRContext? _skiaContext;
    private GRVkBackendContext? _skiaBackendContext;
    private bool _disposed;

    public VulkanContext(VulkanInstance vulkanInstance, VulkanPhysicalDeviceInfo physicalDevice)
    {
        _vulkanInstance = vulkanInstance;
        _vulkanDevice = new VulkanDevice(vulkanInstance.Vk, vulkanInstance.Instance, physicalDevice.Device);
        _vulkanCommandPool = new VulkanCommandPool(
            vulkanInstance.Vk,
            _vulkanDevice.Device,
            _vulkanDevice.GraphicsQueue,
            _vulkanDevice.GraphicsQueueFamilyIndex);

        if (!physicalDevice.IsMoltenVK)
        {
            InitializeSkiaVulkanContext();
        }

        s_logger.LogDebug("Vulkan context created successfully");
    }

    private void InitializeSkiaVulkanContext()
    {
        try
        {
            _skiaBackendContext = new GRVkBackendContext
            {
                VkInstance = _vulkanInstance.Instance.Handle,
                VkPhysicalDevice = _vulkanDevice.PhysicalDevice.Handle,
                VkDevice = _vulkanDevice.Device.Handle,
                VkQueue = _vulkanDevice.GraphicsQueue.Handle,
                GraphicsQueueIndex = _vulkanDevice.GraphicsQueueFamilyIndex,
                GetProcedureAddress = GetVulkanProcAddress
            };

            _skiaContext = GRContext.CreateVulkan(_skiaBackendContext);

            if (_skiaContext == null)
            {
                s_logger.LogWarning("Failed to create SkiaSharp Vulkan context");
            }
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "Failed to initialize SkiaSharp Vulkan backend");
        }
    }

    private IntPtr GetVulkanProcAddress(string name, IntPtr instance, IntPtr device)
    {
        var vk = _vulkanInstance.Vk;

        if (device != IntPtr.Zero)
        {
            var deviceHandle = new Device(device);
            var addr = vk.GetDeviceProcAddr(deviceHandle, name);
            if (addr != IntPtr.Zero)
                return addr;
        }

        if (instance != IntPtr.Zero)
        {
            var instanceHandle = new Instance(instance);
            var addr = vk.GetInstanceProcAddr(instanceHandle, name);
            if (addr != IntPtr.Zero)
                return addr;
        }

        return vk.GetInstanceProcAddr(_vulkanInstance.Instance, name);
    }

    public GraphicsBackend Backend => GraphicsBackend.Vulkan;

    public GRContext SkiaContext => _skiaContext ?? throw new InvalidOperationException(
        "SkiaSharp Vulkan context is not initialized. Make sure the Vulkan context was created successfully.");

    public Vk Vk => _vulkanInstance.Vk;

    public Instance Instance => _vulkanInstance.Instance;

    public PhysicalDevice PhysicalDevice => _vulkanDevice.PhysicalDevice;

    public Device Device => _vulkanDevice.Device;

    public Queue GraphicsQueue => _vulkanDevice.GraphicsQueue;

    public uint GraphicsQueueFamilyIndex => _vulkanDevice.GraphicsQueueFamilyIndex;

    public IEnumerable<string> EnabledExtensions =>
        _vulkanInstance.EnabledExtensions.Concat(_vulkanDevice.EnabledExtensions);

    public bool Supports3DRendering => true;

    public ISharedTexture CreateTexture(int width, int height, TextureFormat format)
    {
        return new VulkanSharedTexture(this, width, height, format);
    }

    public ITexture2D CreateTexture2D(int width, int height, TextureFormat format)
    {
        var usage = format.IsDepthFormat()
            ? ImageUsageFlags.DepthStencilAttachmentBit | ImageUsageFlags.SampledBit
            : ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit;
        return new VulkanTexture2D(this, width, height, format, usage);
    }

    public I3DRenderer Create3DRenderer()
    {
        return new Renderer3D(this);
    }

    public IBuffer CreateBuffer(ulong size, BufferUsage usage, MemoryProperty memoryProperty)
    {
        return new VulkanBuffer(this, size, usage, memoryProperty);
    }

    public IShaderCompiler CreateShaderCompiler()
    {
        return new VulkanShaderCompiler();
    }

    public IRenderPass3D CreateRenderPass3D(IReadOnlyList<TextureFormat> colorFormats, TextureFormat depthFormat = TextureFormat.Depth32Float)
    {
        var vulkanColorFormats = colorFormats.Select(f => f.ToVulkanFormat()).ToList();
        return new VulkanRenderPass3D(this, vulkanColorFormats, depthFormat.ToVulkanFormat());
    }

    public IFramebuffer3D CreateFramebuffer3D(IRenderPass3D renderPass, IReadOnlyList<ITexture2D> colorTextures, ITexture2D depthTexture)
    {
        var vulkanRenderPass = (VulkanRenderPass3D)renderPass;
        var vulkanColorTextures = colorTextures.Cast<VulkanTexture2D>().ToList();
        var vulkanDepthTexture = (VulkanTexture2D)depthTexture;
        return new VulkanFramebuffer3D(this, vulkanRenderPass.Handle, vulkanColorTextures, vulkanDepthTexture);
    }

    public IPipeline3D CreatePipeline3D(
        IRenderPass3D renderPass,
        byte[] vertexShaderSpirv,
        byte[] fragmentShaderSpirv,
        DescriptorBinding[] descriptorBindings)
    {
        var vulkanRenderPass = (VulkanRenderPass3D)renderPass;
        var vulkanBindings = descriptorBindings
            .Select(VulkanFlagConverter.ToVulkan)
            .ToArray();
        return new VulkanPipeline3D(
            this,
            vulkanRenderPass.Handle,
            vertexShaderSpirv,
            fragmentShaderSpirv,
            VulkanVertex3D.GetVertexInputDescription(),
            vulkanBindings);
    }

    public IDescriptorSet CreateDescriptorSet(IPipeline3D pipeline, DescriptorPoolSize[] poolSizes)
    {
        var vulkanPipeline = (VulkanPipeline3D)pipeline;
        var vulkanPoolSizes = poolSizes
            .Select(VulkanFlagConverter.ToVulkan)
            .ToArray();
        return new VulkanDescriptorSet(this, vulkanPipeline.DescriptorSetLayoutHandle, vulkanPoolSizes);
    }

    public unsafe void CopyBuffer(IBuffer source, IBuffer destination, ulong size)
    {
        var vulkanSource = (VulkanBuffer)source;
        var vulkanDest = (VulkanBuffer)destination;

        SubmitImmediateCommands(cmd =>
        {
            var copyRegion = new BufferCopy { Size = size };
            Vk.CmdCopyBuffer(cmd, vulkanSource.Handle, vulkanDest.Handle, 1, &copyRegion);
        });
    }

    public void WaitIdle()
    {
        _vulkanDevice.WaitIdle();
    }

    public void SubmitImmediateCommands(Action<CommandBuffer> record)
    {
        _vulkanCommandPool.SubmitImmediateCommands(record);
    }

    public void TransitionImageLayout(Image image, ImageLayout oldLayout, ImageLayout newLayout)
    {
        _vulkanCommandPool.TransitionImageLayout(image, oldLayout, newLayout);
    }

    public void TransitionImageLayout(Image image, ImageLayout oldLayout, ImageLayout newLayout, ImageAspectFlags aspectMask)
    {
        _vulkanCommandPool.TransitionImageLayout(image, oldLayout, newLayout, aspectMask);
    }

    public CommandBuffer AllocateCommandBuffer()
    {
        return _vulkanCommandPool.AllocateCommandBuffer();
    }

    public void SubmitCommandBuffer(CommandBuffer commandBuffer)
    {
        _vulkanCommandPool.SubmitCommandBuffer(commandBuffer);
    }


    /// <summary>
    /// Finds a suitable memory type for the given requirements.
    /// </summary>
    public unsafe uint FindMemoryType(uint typeFilter, MemoryPropertyFlags properties)
    {
        PhysicalDeviceMemoryProperties memProps;
        Vk.GetPhysicalDeviceMemoryProperties(PhysicalDevice, &memProps);

        for (uint i = 0; i < memProps.MemoryTypeCount; i++)
        {
            if ((typeFilter & (1u << (int)i)) != 0 &&
                (memProps.MemoryTypes[(int)i].PropertyFlags & properties) == properties)
            {
                return i;
            }
        }

        throw new InvalidOperationException("Failed to find suitable memory type");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _vulkanDevice.WaitIdle();

        _skiaContext?.Dispose();
        _skiaContext = null;
        _skiaBackendContext?.Dispose();
        _skiaBackendContext = null;

        _vulkanCommandPool.Dispose();
        _vulkanDevice.Dispose();
    }
}
