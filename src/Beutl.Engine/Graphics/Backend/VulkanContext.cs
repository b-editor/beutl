using System.Runtime.InteropServices;
using System.Text.Json;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;
using SkiaSharp;

namespace Beutl.Graphics.Backend;

using Semaphore = Silk.NET.Vulkan.Semaphore;
using Image = Silk.NET.Vulkan.Image;

internal sealed unsafe class VulkanContext : IGraphicsContext
{
    private static readonly ILogger s_logger = Log.CreateLogger<VulkanContext>();
    private readonly Vk _vk;
    private readonly Instance _instance;
    private readonly PhysicalDevice _physicalDevice;
    private readonly Device _device;
    private readonly Queue _graphicsQueue;
    private readonly uint _graphicsQueueFamilyIndex;
    private readonly ExtDebugUtils? _debugUtils;
    private readonly DebugUtilsMessengerEXT _debugMessenger;
    private readonly bool _enableValidation;
    private readonly string[] _enabledInstanceExtensions;
    private readonly string[] _enabledDeviceExtensions;
    private readonly GpuInfo? _gpuInfo;
    private readonly CommandPool _commandPool;
    private readonly Fence _immediateFence;
    private Semaphore _submissionSemaphore;
    private bool _hasPendingSemaphoreSignal;
    private GRContext? _skiaContext;
    private GRVkBackendContext? _skiaBackendContext;
    private bool _disposed;

    public VulkanContext(bool enableValidation = false)
    {
        // Set icd for Vulkan loader
        VulkanSetup.Setup();

        _vk = Vk.GetApi();
        _enableValidation = enableValidation;

        // Get required instance extensions
        _enabledInstanceExtensions = GetRequiredInstanceExtensions();

        // Create Vulkan instance
        _instance = CreateInstance(_enabledInstanceExtensions);

        // Setup debug messenger if validation is enabled
        if (_enableValidation)
        {
            if (_vk.TryGetInstanceExtension(_instance, out _debugUtils))
            {
                _debugMessenger = CreateDebugMessenger();
            }
        }

        // Select physical device
        _physicalDevice = SelectPhysicalDevice();

        // Find graphics queue family
        _graphicsQueueFamilyIndex = FindGraphicsQueueFamily();

        // Get required device extensions
        _enabledDeviceExtensions = GetRequiredDeviceExtensions();

        // Create logical device
        _device = CreateDevice(_enabledDeviceExtensions);

        // Get graphics queue
        _vk.GetDeviceQueue(_device, _graphicsQueueFamilyIndex, 0, out _graphicsQueue);

        _commandPool = CreateCommandPool();
        _immediateFence = CreateFence();
        _submissionSemaphore = CreateSemaphore();

        // Initialize SkiaSharp Vulkan backend (Windows/Linux only, macOS uses Metal)
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            InitializeSkiaVulkanContext();
        }

        // Collect GPU information
        _gpuInfo = CollectGpuInfo();

        s_logger.LogDebug("Vulkan context created successfully");
    }

    private void InitializeSkiaVulkanContext()
    {
        try
        {
            _skiaBackendContext = new GRVkBackendContext
            {
                VkInstance = _instance.Handle,
                VkPhysicalDevice = _physicalDevice.Handle,
                VkDevice = _device.Handle,
                VkQueue = _graphicsQueue.Handle,
                GraphicsQueueIndex = _graphicsQueueFamilyIndex,
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
        if (device != IntPtr.Zero)
        {
            var deviceHandle = new Device(device);
            var addr = _vk.GetDeviceProcAddr(deviceHandle, name);
            if (addr != IntPtr.Zero)
                return addr;
        }

        if (instance != IntPtr.Zero)
        {
            var instanceHandle = new Instance(instance);
            var addr = _vk.GetInstanceProcAddr(instanceHandle, name);
            if (addr != IntPtr.Zero)
                return addr;
        }

        return _vk.GetInstanceProcAddr(_instance, name);
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

    public Vk Vk => _vk;

    public Instance Instance => _instance;

    public PhysicalDevice PhysicalDevice => _physicalDevice;

    public Device Device => _device;

    public Queue GraphicsQueue => _graphicsQueue;

    public uint GraphicsQueueFamilyIndex => _graphicsQueueFamilyIndex;

    public GpuInfo? GpuInfo => _gpuInfo;

    public ISharedTexture CreateTexture(int width, int height, TextureFormat format)
    {
        return new VulkanSharedTexture(this, width, height, format);
    }

    public void WaitIdle()
    {
        _vk.DeviceWaitIdle(_device);
    }

    private string[] GetRequiredInstanceExtensions()
    {
        var extensions = new List<string>();

        // Surface extension required for presentation
        extensions.Add("VK_KHR_surface");

        // Required for portability on macOS
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            extensions.Add("VK_KHR_portability_enumeration");
            extensions.Add("VK_KHR_get_physical_device_properties2");
            extensions.Add("VK_EXT_metal_objects"); // For Metal interop
            extensions.Add("VK_EXT_metal_surface"); // For creating Vulkan surface from CAMetalLayer
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            extensions.Add("VK_KHR_win32_surface");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Try X11 and Wayland surface extensions
            extensions.Add("VK_KHR_xlib_surface");
            extensions.Add("VK_KHR_xcb_surface");
            extensions.Add("VK_KHR_wayland_surface");
        }

        // Debug utils if validation is enabled
        if (_enableValidation)
        {
            extensions.Add(ExtDebugUtils.ExtensionName);
        }

        return extensions.ToArray();
    }

    private string[] GetRequiredDeviceExtensions()
    {
        var extensions = new List<string>();

        // Check available extensions
        uint extensionCount = 0;
        _vk.EnumerateDeviceExtensionProperties(_physicalDevice, (byte*)null, &extensionCount, null);

        var availableExtensions = new ExtensionProperties[extensionCount];
        fixed (ExtensionProperties* pExtensions = availableExtensions)
        {
            _vk.EnumerateDeviceExtensionProperties(_physicalDevice, (byte*)null, &extensionCount, pExtensions);
        }

        var availableNames = new HashSet<string>();
        foreach (var ext in availableExtensions)
        {
            var extName = Marshal.PtrToStringAnsi((IntPtr)ext.ExtensionName);
            if (!string.IsNullOrEmpty(extName))
                availableNames.Add(extName);
        }

        // Swapchain extension required for presentation
        if (availableNames.Contains("VK_KHR_swapchain"))
            extensions.Add("VK_KHR_swapchain");

        // On macOS with MoltenVK
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (availableNames.Contains("VK_KHR_portability_subset"))
                extensions.Add("VK_KHR_portability_subset");

            if (availableNames.Contains("VK_EXT_metal_objects"))
                extensions.Add("VK_EXT_metal_objects");
        }

        return extensions.ToArray();
    }

    private Instance CreateInstance(string[] extensions)
    {
        // Check validation layer support
        string[] validationLayers = Array.Empty<string>();
        if (_enableValidation)
        {
            validationLayers = new[] { "VK_LAYER_KHRONOS_validation" };
            if (!CheckValidationLayerSupport(validationLayers))
            {
                s_logger.LogWarning("Validation layers requested but not available, continuing without them.");
                validationLayers = Array.Empty<string>();
            }
        }

        // Filter to available extensions
        var availableExtensions = EnumerateInstanceExtensions();
        var filteredExtensions = extensions.Where(e => availableExtensions.Contains(e)).ToArray();

        // Create application info
        var appNamePtr = Marshal.StringToHGlobalAnsi("Beutl");
        var engineNamePtr = Marshal.StringToHGlobalAnsi("Beutl.Engine");

        try
        {
            var appInfo = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = (byte*)appNamePtr,
                ApplicationVersion = new Version32(1, 0, 0),
                PEngineName = (byte*)engineNamePtr,
                EngineVersion = new Version32(1, 0, 0),
                ApiVersion = Vk.Version12
            };

            var createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo, PApplicationInfo = &appInfo
            };

            // Setup extensions
            var extensionPtrs = new byte*[filteredExtensions.Length];
            for (int i = 0; i < filteredExtensions.Length; i++)
            {
                extensionPtrs[i] = (byte*)Marshal.StringToHGlobalAnsi(filteredExtensions[i]);
            }

            fixed (byte** ppExtensions = extensionPtrs)
            {
                createInfo.EnabledExtensionCount = (uint)filteredExtensions.Length;
                createInfo.PpEnabledExtensionNames = ppExtensions;

                // Setup validation layers
                var layerPtrs = new byte*[validationLayers.Length];
                for (int i = 0; i < validationLayers.Length; i++)
                {
                    layerPtrs[i] = (byte*)Marshal.StringToHGlobalAnsi(validationLayers[i]);
                }

                fixed (byte** ppLayers = layerPtrs)
                {
                    createInfo.EnabledLayerCount = (uint)validationLayers.Length;
                    createInfo.PpEnabledLayerNames = ppLayers;

                    // Add flags for macOS
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        createInfo.Flags |= InstanceCreateFlags.EnumeratePortabilityBitKhr;
                    }

                    Instance instance;
                    var result = _vk.CreateInstance(&createInfo, null, &instance);
                    if (result != Result.Success)
                    {
                        throw new InvalidOperationException($"Failed to create Vulkan instance: {result}");
                    }

                    // Cleanup layer pointers
                    for (int i = 0; i < validationLayers.Length; i++)
                    {
                        Marshal.FreeHGlobal((IntPtr)layerPtrs[i]);
                    }

                    // Cleanup extension pointers
                    for (int i = 0; i < filteredExtensions.Length; i++)
                    {
                        Marshal.FreeHGlobal((IntPtr)extensionPtrs[i]);
                    }

                    return instance;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(appNamePtr);
            Marshal.FreeHGlobal(engineNamePtr);
        }
    }

    private HashSet<string> EnumerateInstanceExtensions()
    {
        uint count = 0;
        _vk.EnumerateInstanceExtensionProperties((byte*)null, &count, null);

        var extensions = new ExtensionProperties[count];
        fixed (ExtensionProperties* pExtensions = extensions)
        {
            _vk.EnumerateInstanceExtensionProperties((byte*)null, &count, pExtensions);
        }

        var result = new HashSet<string>();
        foreach (var ext in extensions)
        {
            var name = Marshal.PtrToStringAnsi((IntPtr)ext.ExtensionName);
            if (!string.IsNullOrEmpty(name))
                result.Add(name);
        }

        return result;
    }

    private bool CheckValidationLayerSupport(string[] validationLayers)
    {
        uint layerCount = 0;
        _vk.EnumerateInstanceLayerProperties(&layerCount, null);

        var availableLayers = new LayerProperties[layerCount];
        fixed (LayerProperties* pLayers = availableLayers)
        {
            _vk.EnumerateInstanceLayerProperties(&layerCount, pLayers);
        }

        foreach (var layerName in validationLayers)
        {
            bool found = false;
            foreach (var layer in availableLayers)
            {
                var name = Marshal.PtrToStringAnsi((IntPtr)layer.LayerName);
                if (name == layerName)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                return false;
        }

        return true;
    }

    private DebugUtilsMessengerEXT CreateDebugMessenger()
    {
        var createInfo = new DebugUtilsMessengerCreateInfoEXT
        {
            SType = StructureType.DebugUtilsMessengerCreateInfoExt,
            MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.WarningBitExt |
                              DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt,
            MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt |
                          DebugUtilsMessageTypeFlagsEXT.ValidationBitExt |
                          DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt,
            PfnUserCallback = (PfnDebugUtilsMessengerCallbackEXT)DebugCallback
        };

        DebugUtilsMessengerEXT messenger;
        var result = _debugUtils!.CreateDebugUtilsMessenger(_instance, &createInfo, null, &messenger);
        if (result != Result.Success)
        {
            s_logger.LogError("Failed to create debug messenger: {Result}", result);
        }

        return messenger;
    }

    private static uint DebugCallback(
        DebugUtilsMessageSeverityFlagsEXT severity,
        DebugUtilsMessageTypeFlagsEXT type,
        DebugUtilsMessengerCallbackDataEXT* callbackData,
        void* userData)
    {
        var message = Marshal.PtrToStringAnsi((IntPtr)callbackData->PMessage);
        switch (severity)
        {
#pragma warning disable CA2254, CA1873
            case DebugUtilsMessageSeverityFlagsEXT.None:
                break;
            case DebugUtilsMessageSeverityFlagsEXT.VerboseBitExt:
                s_logger.LogDebug(message);
                break;
            case DebugUtilsMessageSeverityFlagsEXT.InfoBitExt:
                s_logger.LogInformation(message);
                break;
            case DebugUtilsMessageSeverityFlagsEXT.WarningBitExt:
                s_logger.LogWarning(message);
                break;
            case DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt:
                s_logger.LogError(message);
                break;
            default:
                s_logger.LogInformation($"Unknown severity({severity}): {message}");
                break;
#pragma warning restore CA2254, CA1873
        }

        return Vk.False;
    }

    private PhysicalDevice SelectPhysicalDevice()
    {
        uint deviceCount = 0;
        _vk.EnumeratePhysicalDevices(_instance, &deviceCount, null);

        if (deviceCount == 0)
        {
            throw new InvalidOperationException("No Vulkan-capable GPU found");
        }

        var devices = new PhysicalDevice[deviceCount];
        fixed (PhysicalDevice* pDevices = devices)
        {
            _vk.EnumeratePhysicalDevices(_instance, &deviceCount, pDevices);
        }

        // Select the first suitable device (prefer discrete GPU)
        PhysicalDevice selectedDevice = default;
        bool foundDiscrete = false;
        bool foundIntegrated = false;

        foreach (var device in devices)
        {
            PhysicalDeviceProperties properties;
            _vk.GetPhysicalDeviceProperties(device, &properties);

            var deviceName = Marshal.PtrToStringAnsi((IntPtr)properties.DeviceName);
            s_logger.LogInformation("Found GPU: {DeviceName} (Type: {DeviceType})", deviceName, properties.DeviceType);

            if (properties.DeviceType == PhysicalDeviceType.DiscreteGpu && !foundDiscrete)
            {
                selectedDevice = device;
                foundDiscrete = true;
                foundIntegrated = true;
            }
            else if (properties.DeviceType == PhysicalDeviceType.IntegratedGpu && !foundIntegrated)
            {
                selectedDevice = device;
                foundIntegrated = true;
            }
            else if (selectedDevice.Handle == IntPtr.Zero)
            {
                selectedDevice = device;
            }
        }

        PhysicalDeviceProperties selectedProps;
        _vk.GetPhysicalDeviceProperties(selectedDevice, &selectedProps);
        var selectedName = Marshal.PtrToStringAnsi((IntPtr)selectedProps.DeviceName);
        s_logger.LogInformation("Selected GPU: {SelectedName}", selectedName);

        return selectedDevice;
    }

    private uint FindGraphicsQueueFamily()
    {
        uint queueFamilyCount = 0;
        _vk.GetPhysicalDeviceQueueFamilyProperties(_physicalDevice, &queueFamilyCount, null);

        var queueFamilies = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* pQueueFamilies = queueFamilies)
        {
            _vk.GetPhysicalDeviceQueueFamilyProperties(_physicalDevice, &queueFamilyCount, pQueueFamilies);
        }

        for (uint i = 0; i < queueFamilyCount; i++)
        {
            if ((queueFamilies[i].QueueFlags & QueueFlags.GraphicsBit) != 0)
            {
                return i;
            }
        }

        throw new InvalidOperationException("No graphics queue family found");
    }

    private Device CreateDevice(string[] extensions)
    {
        float queuePriority = 1.0f;
        var queueCreateInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = _graphicsQueueFamilyIndex,
            QueueCount = 1,
            PQueuePriorities = &queuePriority
        };

        var features = new PhysicalDeviceFeatures();

        var createInfo = new DeviceCreateInfo
        {
            SType = StructureType.DeviceCreateInfo,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueCreateInfo,
            PEnabledFeatures = &features
        };

        // Setup extensions
        var extensionPtrs = new byte*[extensions.Length];
        for (int i = 0; i < extensions.Length; i++)
        {
            extensionPtrs[i] = (byte*)Marshal.StringToHGlobalAnsi(extensions[i]);
        }

        try
        {
            fixed (byte** ppExtensions = extensionPtrs)
            {
                createInfo.EnabledExtensionCount = (uint)extensions.Length;
                createInfo.PpEnabledExtensionNames = ppExtensions;

                Device device;
                var result = _vk.CreateDevice(_physicalDevice, &createInfo, null, &device);
                if (result != Result.Success)
                {
                    throw new InvalidOperationException($"Failed to create Vulkan device: {result}");
                }

                return device;
            }
        }
        finally
        {
            for (int i = 0; i < extensions.Length; i++)
            {
                Marshal.FreeHGlobal((IntPtr)extensionPtrs[i]);
            }
        }
    }

    private CommandPool CreateCommandPool()
    {
        var createInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = _graphicsQueueFamilyIndex,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit |
                    CommandPoolCreateFlags.TransientBit
        };

        CommandPool pool;
        var result = _vk.CreateCommandPool(_device, &createInfo, null, &pool);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Failed to create command pool: {result}");
        }

        return pool;
    }

    private Fence CreateFence()
    {
        var createInfo = new FenceCreateInfo { SType = StructureType.FenceCreateInfo };

        Fence fence;
        var result = _vk.CreateFence(_device, &createInfo, null, &fence);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Failed to create fence: {result}");
        }

        return fence;
    }

    private Semaphore CreateSemaphore()
    {
        var createInfo = new SemaphoreCreateInfo { SType = StructureType.SemaphoreCreateInfo };

        Semaphore semaphore;
        var result = _vk.CreateSemaphore(_device, &createInfo, null, &semaphore);
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Failed to create semaphore: {result}");
        }

        return semaphore;
    }

    public void SubmitImmediateCommands(Action<CommandBuffer> record)
    {
        CommandBufferAllocateInfo allocInfo = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };

        CommandBuffer commandBuffer;
        _vk.AllocateCommandBuffers(_device, &allocInfo, &commandBuffer);

        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo, Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        _vk.BeginCommandBuffer(commandBuffer, &beginInfo);
        record(commandBuffer);
        _vk.EndCommandBuffer(commandBuffer);

        SubmitInfo submitInfo = new()
        {
            SType = StructureType.SubmitInfo, CommandBufferCount = 1, PCommandBuffers = &commandBuffer
        };

        fixed (Semaphore* submissionSemaphore = &_submissionSemaphore)
        fixed (Fence* immediateFence = &_immediateFence)
        {
            PipelineStageFlags waitDstStageMask = PipelineStageFlags.AllCommandsBit;
            if (_hasPendingSemaphoreSignal)
            {
                submitInfo.WaitSemaphoreCount = 1;
                submitInfo.PWaitSemaphores = submissionSemaphore;
                submitInfo.PWaitDstStageMask = &waitDstStageMask;
            }

            submitInfo.SignalSemaphoreCount = 1;
            submitInfo.PSignalSemaphores = submissionSemaphore;

            _vk.ResetFences(_device, 1, immediateFence);
            _vk.QueueSubmit(_graphicsQueue, 1, &submitInfo, _immediateFence);
            _vk.WaitForFences(_device, 1, immediateFence, Vk.True, ulong.MaxValue);

            _hasPendingSemaphoreSignal = true;
            _vk.FreeCommandBuffers(_device, _commandPool, 1, &commandBuffer);
        }
    }

    public void TransitionImageLayout(Image image, ImageLayout oldLayout, ImageLayout newLayout)
    {
        SubmitImmediateCommands(commandBuffer =>
        {
            ImageMemoryBarrier barrier = new()
            {
                SType = StructureType.ImageMemoryBarrier,
                OldLayout = oldLayout,
                NewLayout = newLayout,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = image,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1
                }
            };

            GetPipelineStages(oldLayout, newLayout, out PipelineStageFlags srcStage, out PipelineStageFlags dstStage,
                out AccessFlags srcAccess, out AccessFlags dstAccess);

            barrier.SrcAccessMask = srcAccess;
            barrier.DstAccessMask = dstAccess;

            _vk.CmdPipelineBarrier(commandBuffer, srcStage, dstStage, 0, 0, null, 0, null, 1, &barrier);
        });
    }

    private static void GetPipelineStages(
        ImageLayout oldLayout,
        ImageLayout newLayout,
        out PipelineStageFlags srcStage,
        out PipelineStageFlags dstStage,
        out AccessFlags srcAccess,
        out AccessFlags dstAccess)
    {
        srcStage = PipelineStageFlags.TopOfPipeBit;
        dstStage = PipelineStageFlags.BottomOfPipeBit;
        srcAccess = 0;
        dstAccess = 0;

        if (oldLayout == ImageLayout.Undefined && newLayout == ImageLayout.ColorAttachmentOptimal)
        {
            srcStage = PipelineStageFlags.TopOfPipeBit;
            dstStage = PipelineStageFlags.ColorAttachmentOutputBit;
            srcAccess = 0;
            dstAccess = AccessFlags.ColorAttachmentWriteBit;
        }
        else if (oldLayout == ImageLayout.ColorAttachmentOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            srcStage = PipelineStageFlags.ColorAttachmentOutputBit;
            dstStage = PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.ComputeShaderBit;
            srcAccess = AccessFlags.ColorAttachmentWriteBit;
            dstAccess = AccessFlags.ShaderReadBit;
        }
        else if (oldLayout == ImageLayout.ShaderReadOnlyOptimal && newLayout == ImageLayout.ColorAttachmentOptimal)
        {
            srcStage = PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.ComputeShaderBit;
            dstStage = PipelineStageFlags.ColorAttachmentOutputBit;
            srcAccess = AccessFlags.ShaderReadBit;
            dstAccess = AccessFlags.ColorAttachmentWriteBit;
        }
        else if (oldLayout == ImageLayout.ColorAttachmentOptimal && newLayout == ImageLayout.TransferSrcOptimal)
        {
            srcStage = PipelineStageFlags.ColorAttachmentOutputBit;
            dstStage = PipelineStageFlags.TransferBit;
            srcAccess = AccessFlags.ColorAttachmentWriteBit;
            dstAccess = AccessFlags.TransferReadBit;
        }
        else if (oldLayout == ImageLayout.TransferSrcOptimal && newLayout == ImageLayout.ColorAttachmentOptimal)
        {
            srcStage = PipelineStageFlags.TransferBit;
            dstStage = PipelineStageFlags.ColorAttachmentOutputBit;
            srcAccess = AccessFlags.TransferReadBit;
            dstAccess = AccessFlags.ColorAttachmentWriteBit;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _vk.DeviceWaitIdle(_device);

        // Dispose SkiaSharp context first
        _skiaContext?.Dispose();
        _skiaContext = null;
        _skiaBackendContext?.Dispose();
        _skiaBackendContext = null;

        if (_immediateFence.Handle != 0)
        {
            _vk.DestroyFence(_device, _immediateFence, null);
        }

        if (_commandPool.Handle != 0)
        {
            _vk.DestroyCommandPool(_device, _commandPool, null);
        }

        if (_submissionSemaphore.Handle != 0)
        {
            _vk.DestroySemaphore(_device, _submissionSemaphore, null);
        }

        _vk.DestroyDevice(_device, null);

        if (_enableValidation && _debugMessenger.Handle != 0 && _debugUtils != null)
        {
            _debugUtils.DestroyDebugUtilsMessenger(_instance, _debugMessenger, null);
        }

        _vk.DestroyInstance(_instance, null);
        _vk.Dispose();
    }

    private GpuInfo CollectGpuInfo()
    {
        var availableGpus = new List<GpuDeviceInfo>();
        GpuDeviceInfo? selectedGpu = null;
        GpuMemoryInfo? memoryInfo = null;
        string apiVersion = "Unknown";

        // Enumerate all physical devices
        uint deviceCount = 0;
        _vk.EnumeratePhysicalDevices(_instance, &deviceCount, null);

        if (deviceCount > 0)
        {
            var devices = new PhysicalDevice[deviceCount];
            fixed (PhysicalDevice* pDevices = devices)
            {
                _vk.EnumeratePhysicalDevices(_instance, &deviceCount, pDevices);
            }

            foreach (var device in devices)
            {
                PhysicalDeviceProperties properties;
                _vk.GetPhysicalDeviceProperties(device, &properties);

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

                // If this is the selected device, get additional info
                if (device.Handle == _physicalDevice.Handle)
                {
                    selectedGpu = new GpuDeviceInfo(deviceName, deviceType);

                    // Extract Vulkan API version
                    uint major = properties.ApiVersion >> 22;
                    uint minor = (properties.ApiVersion >> 12) & 0x3FF;
                    uint patch = properties.ApiVersion & 0xFFF;
                    apiVersion = $"{major}.{minor}.{patch}";

                    // Get memory properties
                    PhysicalDeviceMemoryProperties memProps;
                    _vk.GetPhysicalDeviceMemoryProperties(device, &memProps);

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

        // Combine instance and device extensions
        var allExtensions = new List<string>();
        allExtensions.AddRange(_enabledInstanceExtensions);
        allExtensions.AddRange(_enabledDeviceExtensions);

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
