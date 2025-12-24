using System.Runtime.InteropServices;
using System.Text.Json;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using SkiaSharp;

namespace Beutl.Graphics.Backend;

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

    public ISharedTexture CreateTexture(int width, int height, TextureFormat format)
    {
        return new VulkanSharedTexture(this, width, height, format);
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
