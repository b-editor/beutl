using System.Runtime.InteropServices;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;

namespace Beutl.Graphics.Backend.Vulkan;

internal record VulkanMemoryInfo(ulong DeviceLocalMemory, ulong HostVisibleMemory);

internal sealed unsafe class VulkanInstance : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<VulkanInstance>();

    // The API version the instance is created for; Skia is told not to use anything newer.
    internal static readonly Version32 InstanceApiVersion = Vk.Version12;

    private readonly Vk _vk;
    private readonly Instance _instance;
    private readonly ExtDebugUtils? _debugUtils;
    private readonly DebugUtilsMessengerEXT _debugMessenger;
    private readonly bool _enableValidation;
    private readonly string[] _enabledExtensions;
    private bool _disposed;

    public VulkanInstance(Vk vk, bool enableValidation)
    {
        _vk = vk;
        _enableValidation = enableValidation;
        _enabledExtensions = GetRequiredInstanceExtensions();
        _instance = CreateInstance(_enabledExtensions);

        if (_enableValidation)
        {
            try
            {
                if (!_vk.TryGetInstanceExtension(_instance, out _debugUtils))
                {
                    throw new InvalidOperationException(
                        $"Vulkan validation was requested, but {ExtDebugUtils.ExtensionName} could not be loaded.");
                }

                _debugMessenger = CreateDebugMessenger();
            }
            catch
            {
                _vk.DestroyInstance(_instance, null);
                throw;
            }
        }
    }

    public Vk Vk => _vk;

    public Instance Instance => _instance;

    public bool EnableValidation => _enableValidation;

    public string[] EnabledExtensions => _enabledExtensions;

    public PhysicalDevice[] EnumeratePhysicalDevices()
    {
        uint deviceCount = 0;
        _vk.EnumeratePhysicalDevices(_instance, &deviceCount, null);

        if (deviceCount == 0)
        {
            return [];
        }

        var devices = new PhysicalDevice[deviceCount];
        fixed (PhysicalDevice* pDevices = devices)
        {
            _vk.EnumeratePhysicalDevices(_instance, &deviceCount, pDevices);
        }

        return devices;
    }

    /// <summary>Gets the devices the engine can render on, which are the ones it offers and chooses from.</summary>
    public VulkanPhysicalDeviceInfo[] GetAvailableGpus()
    {
        var devices = EnumeratePhysicalDevices();
        var result = new VulkanPhysicalDeviceInfo[devices.Length];

        for (int i = 0; i < devices.Length; i++)
        {
            result[i] = GetPhysicalDeviceDetails(devices[i]);
        }

        return FilterRenderableDevices(result, OperatingSystem.IsMacOS());
    }

    /// <summary>Leaves out the devices the engine cannot render on.</summary>
    /// <remarks>
    /// On macOS Skia renders through Metal, because Beutl's libSkiaSharp is built without Vulkan there, and only a
    /// device MoltenVK drives has a Metal device behind it. A context on any other driver, SwiftShader for one, would
    /// have no Skia GPU context, so such a device is neither offered for selection nor chosen automatically.
    /// </remarks>
    /// <param name="gpus">Every device the instance enumerated.</param>
    /// <param name="macOS">Whether the process runs on macOS.</param>
    internal static VulkanPhysicalDeviceInfo[] FilterRenderableDevices(VulkanPhysicalDeviceInfo[] gpus, bool macOS)
    {
        if (!macOS)
            return gpus;

        foreach (var gpu in gpus)
        {
            if (!gpu.IsMoltenVK)
            {
                s_logger.LogDebug(
                    "Leaving out GPU {DeviceName} ({DriverId}): on macOS only a MoltenVK device can back Skia.",
                    gpu.Name,
                    gpu.DriverId);
            }
        }

        return Array.FindAll(gpus, static gpu => gpu.IsMoltenVK);
    }

    public VulkanPhysicalDeviceInfo SelectBestPhysicalDevice()
        => SelectBest(GetAvailableGpus()) ?? throw new InvalidOperationException("No Vulkan-capable GPU found");

    /// <summary>Selects the device to render on from <paramref name="gpus"/>, or <see langword="null"/> when there is none.</summary>
    /// <remarks>
    /// A working instance can leave nothing to select: on a Mac whose only device is SwiftShader,
    /// <see cref="FilterRenderableDevices"/> leaves the list empty, and the shared context renders on the CPU.
    /// </remarks>
    internal static VulkanPhysicalDeviceInfo? SelectBest(VulkanPhysicalDeviceInfo[] gpus)
    {
        if (gpus.Length == 0)
        {
            return null;
        }

        VulkanPhysicalDeviceInfo? selected = null;

        foreach (var gpu in gpus)
        {
            s_logger.LogInformation("Found GPU: {DeviceName} (Type: {DeviceType})", gpu.Name, gpu.Type);

            if (gpu.Type == PhysicalDeviceType.DiscreteGpu)
            {
                selected = gpu;
                break;
            }

            if (gpu.Type == PhysicalDeviceType.IntegratedGpu && selected == null)
            {
                selected = gpu;
            }
            else if (selected == null)
            {
                selected = gpu;
            }
        }

        s_logger.LogInformation("Selected GPU: {DeviceName}", selected!.Name);
        return selected;
    }

    public VulkanPhysicalDeviceInfo GetPhysicalDeviceDetails(PhysicalDevice device)
    {
        PhysicalDeviceProperties properties;
        _vk.GetPhysicalDeviceProperties(device, &properties);

        var deviceName = Marshal.PtrToStringAnsi((IntPtr)properties.DeviceName) ?? "Unknown";
        var deviceType = properties.DeviceType;

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

        var memoryInfo = new VulkanMemoryInfo(deviceLocalMemory, hostVisibleMemory);

        return new VulkanPhysicalDeviceInfo(
            device, deviceName, deviceType, properties.ApiVersion, memoryInfo, GetDriverId(device, properties.ApiVersion));
    }

    /// <summary>Reads which driver implements <paramref name="device"/>, or <see langword="default"/> when it cannot say.</summary>
    private DriverId GetDriverId(PhysicalDevice device, uint apiVersion)
    {
        // The driver properties are core from Vulkan 1.2 and come from VK_KHR_driver_properties on a 1.1 device.
        // A device with neither does not know the structure, and chaining it anyway is not valid usage.
        if (apiVersion < Vk.Version12
            && (apiVersion < Vk.Version11
                || !VulkanPhysicalDeviceQueries.GetDeviceExtensionNames(_vk, device).Contains("VK_KHR_driver_properties")))
        {
            return default;
        }

        var driverProperties = new PhysicalDeviceDriverProperties
        {
            SType = StructureType.PhysicalDeviceDriverProperties,
        };
        var properties2 = new PhysicalDeviceProperties2
        {
            SType = StructureType.PhysicalDeviceProperties2,
            PNext = &driverProperties,
        };
        _vk.GetPhysicalDeviceProperties2(device, &properties2);
        return driverProperties.DriverID;
    }

    private string[] GetRequiredInstanceExtensions()
    {
        var extensions = new List<string>();

        extensions.Add("VK_KHR_surface");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            extensions.Add("VK_KHR_portability_enumeration");
            extensions.Add("VK_KHR_get_physical_device_properties2");
            extensions.Add("VK_EXT_metal_objects");
            extensions.Add("VK_EXT_metal_surface");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            extensions.Add("VK_KHR_win32_surface");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            extensions.Add("VK_KHR_xlib_surface");
            extensions.Add("VK_KHR_xcb_surface");
            extensions.Add("VK_KHR_wayland_surface");
        }

        // HDR swapchain color space negotiation
        extensions.Add("VK_EXT_swapchain_colorspace");

        if (_enableValidation)
        {
            extensions.Add(ExtDebugUtils.ExtensionName);
        }

        return extensions.ToArray();
    }

    private Instance CreateInstance(string[] extensions)
    {
        string[] validationLayers = Array.Empty<string>();
        if (_enableValidation)
        {
            validationLayers = new[] { "VK_LAYER_KHRONOS_validation" };
            if (!CheckValidationLayerSupport(validationLayers))
            {
                throw new InvalidOperationException(
                    "Vulkan validation was requested, but VK_LAYER_KHRONOS_validation is not available.");
            }
        }

        var availableExtensions = EnumerateInstanceExtensions();
        if (_enableValidation && !availableExtensions.Contains(ExtDebugUtils.ExtensionName))
        {
            throw new InvalidOperationException(
                $"Vulkan validation was requested, but {ExtDebugUtils.ExtensionName} is not available.");
        }
        var filteredExtensions = extensions.Where(e => availableExtensions.Contains(e)).ToArray();

        var appNamePtr = Marshal.StringToHGlobalAnsi("Beutl");
        var engineNamePtr = Marshal.StringToHGlobalAnsi("Beutl.Engine");

        var extensionPtrs = new byte*[filteredExtensions.Length];
        var layerPtrs = new byte*[validationLayers.Length];

        try
        {
            for (int i = 0; i < filteredExtensions.Length; i++)
            {
                extensionPtrs[i] = (byte*)Marshal.StringToHGlobalAnsi(filteredExtensions[i]);
            }

            for (int i = 0; i < validationLayers.Length; i++)
            {
                layerPtrs[i] = (byte*)Marshal.StringToHGlobalAnsi(validationLayers[i]);
            }

            var appInfo = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = (byte*)appNamePtr,
                ApplicationVersion = new Version32(1, 0, 0),
                PEngineName = (byte*)engineNamePtr,
                EngineVersion = new Version32(1, 0, 0),
                ApiVersion = InstanceApiVersion
            };

            var createInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo
            };

            fixed (byte** ppExtensions = extensionPtrs)
            fixed (byte** ppLayers = layerPtrs)
            {
                createInfo.EnabledExtensionCount = (uint)filteredExtensions.Length;
                createInfo.PpEnabledExtensionNames = ppExtensions;
                createInfo.EnabledLayerCount = (uint)validationLayers.Length;
                createInfo.PpEnabledLayerNames = ppLayers;

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

                return instance;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(appNamePtr);
            Marshal.FreeHGlobal(engineNamePtr);

            for (int i = 0; i < extensionPtrs.Length; i++)
            {
                if (extensionPtrs[i] != null)
                {
                    Marshal.FreeHGlobal((IntPtr)extensionPtrs[i]);
                }
            }

            for (int i = 0; i < layerPtrs.Length; i++)
            {
                if (layerPtrs[i] != null)
                {
                    Marshal.FreeHGlobal((IntPtr)layerPtrs[i]);
                }
            }
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

        return VulkanPhysicalDeviceQueries.ToNameSet(extensions);
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
            throw new InvalidOperationException($"Failed to create Vulkan debug messenger: {result}");
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
        if ((severity & DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt) != 0
            && (type & DebugUtilsMessageTypeFlagsEXT.ValidationBitExt) != 0)
        {
            try
            {
                VulkanValidationErrorLog.Shared.Record(message);
            }
            catch
            {
                // Never let a managed exception cross the unmanaged Vulkan callback boundary. Losing the
                // record is better than tearing down the driver's reporting thread.
            }
        }

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

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_enableValidation && _debugMessenger.Handle != 0 && _debugUtils != null)
        {
            _debugUtils.DestroyDebugUtilsMessenger(_instance, _debugMessenger, null);
        }

        _vk.DestroyInstance(_instance, null);
    }
}
