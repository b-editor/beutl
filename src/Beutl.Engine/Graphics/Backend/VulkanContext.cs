using System.Runtime.InteropServices;
using System.Text.Json;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using SkiaSharp;

namespace Beutl.Graphics.Backend;

using Image = Silk.NET.Vulkan.Image;

internal sealed unsafe class VulkanContext : IGraphicsContext
{
    private static readonly ILogger s_logger = Log.CreateLogger<VulkanContext>();
    private readonly VulkanInstance _vulkanInstance;
    private readonly VulkanDevice _vulkanDevice;
    private readonly VulkanCommandPool _vulkanCommandPool;
    private readonly GpuInfo? _gpuInfo;
    private GRContext? _skiaContext;
    private GRVkBackendContext? _skiaBackendContext;
    private bool _disposed;

    public VulkanContext(bool enableValidation = false)
    {
        VulkanSetup.Setup();

        var vk = Vk.GetApi();

        _vulkanInstance = new VulkanInstance(vk, enableValidation);
        _vulkanDevice = new VulkanDevice(vk, _vulkanInstance.Instance);
        _vulkanCommandPool = new VulkanCommandPool(
            vk,
            _vulkanDevice.Device,
            _vulkanDevice.GraphicsQueue,
            _vulkanDevice.GraphicsQueueFamilyIndex);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            InitializeSkiaVulkanContext();
        }

        _gpuInfo = CollectGpuInfo();

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

    public GRContext SkiaContext
    {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                throw new PlatformNotSupportedException(
                    "SkiaSharp Vulkan backend is not supported on macOS. Use MetalContext instead, or use VulkanContext for offscreen rendering.");
            }

            return _skiaContext ?? throw new InvalidOperationException(
                "SkiaSharp Vulkan context is not initialized. Make sure the Vulkan context was created successfully.");
        }
    }

    public Vk Vk => _vulkanInstance.Vk;

    public Instance Instance => _vulkanInstance.Instance;

    public PhysicalDevice PhysicalDevice => _vulkanDevice.PhysicalDevice;

    public Device Device => _vulkanDevice.Device;

    public Queue GraphicsQueue => _vulkanDevice.GraphicsQueue;

    public uint GraphicsQueueFamilyIndex => _vulkanDevice.GraphicsQueueFamilyIndex;

    public GpuInfo? GpuInfo => _gpuInfo;

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
        _vulkanInstance.Dispose();
        _vulkanInstance.Vk.Dispose();
    }

    private GpuInfo CollectGpuInfo()
    {
        var vk = _vulkanInstance.Vk;
        var instance = _vulkanInstance.Instance;
        var physicalDevice = _vulkanDevice.PhysicalDevice;

        var availableGpus = new List<GpuDeviceInfo>();
        GpuDeviceInfo? selectedGpu = null;
        GpuMemoryInfo? memoryInfo = null;
        string apiVersion = "Unknown";

        uint deviceCount = 0;
        vk.EnumeratePhysicalDevices(instance, &deviceCount, null);

        if (deviceCount > 0)
        {
            var devices = new PhysicalDevice[deviceCount];
            fixed (PhysicalDevice* pDevices = devices)
            {
                vk.EnumeratePhysicalDevices(instance, &deviceCount, pDevices);
            }

            foreach (var device in devices)
            {
                PhysicalDeviceProperties properties;
                vk.GetPhysicalDeviceProperties(device, &properties);

                var deviceName = Marshal.PtrToStringAnsi((IntPtr)properties.DeviceName) ?? "Unknown";
                var deviceType = properties.DeviceType switch
                {
                    PhysicalDeviceType.IntegratedGpu => "Integrated GPU",
                    PhysicalDeviceType.DiscreteGpu => "Discrete GPU",
                    PhysicalDeviceType.VirtualGpu => "Virtual GPU",
                    PhysicalDeviceType.Cpu => "CPU",
                    _ => "Other"
                };

                availableGpus.Add(new GpuDeviceInfo(deviceName, deviceType));

                if (device.Handle == physicalDevice.Handle)
                {
                    selectedGpu = new GpuDeviceInfo(deviceName, deviceType);

                    uint major = properties.ApiVersion >> 22;
                    uint minor = (properties.ApiVersion >> 12) & 0x3FF;
                    uint patch = properties.ApiVersion & 0xFFF;
                    apiVersion = $"{major}.{minor}.{patch}";

                    PhysicalDeviceMemoryProperties memProps;
                    vk.GetPhysicalDeviceMemoryProperties(device, &memProps);

                    ulong deviceLocalMemory = 0;
                    ulong hostVisibleMemory = 0;

                    for (uint i = 0; i < memProps.MemoryHeapCount; i++)
                    {
                        var heap = memProps.MemoryHeaps[(int)i];
                        if ((heap.Flags & MemoryHeapFlags.DeviceLocalBit) != 0)
                        {
                            deviceLocalMemory += heap.Size;
                        }
                        else
                        {
                            hostVisibleMemory += heap.Size;
                        }
                    }

                    memoryInfo = new GpuMemoryInfo(deviceLocalMemory, hostVisibleMemory);
                }
            }
        }

        var allExtensions = new List<string>();
        allExtensions.AddRange(_vulkanInstance.EnabledExtensions);
        allExtensions.AddRange(_vulkanDevice.EnabledExtensions);

        var info = new GpuInfo(
            availableGpus,
            selectedGpu,
            allExtensions,
            apiVersion,
            memoryInfo);
        var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
        s_logger.LogInformation("GPU Info: {Info}", json);
        return info;
    }
}
