using System.Runtime.InteropServices;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;

namespace Beutl.Graphics.Backend;

internal record VulkanMemoryInfo(ulong DeviceLocalMemory, ulong HostVisibleMemory);

internal record VulkanPhysicalDeviceInfo(
    PhysicalDevice Device,
    string Name,
    PhysicalDeviceType Type,
    uint ApiVersionInt,
    VulkanMemoryInfo Memory)
{
    public bool IsMoltenVK => Name.Contains("Apple");

    public string ApiVersion
    {
        get
        {
            uint major = ApiVersionInt >> 22;
            uint minor = (ApiVersionInt >> 12) & 0x3FF;
            uint patch = ApiVersionInt & 0xFFF;
            return $"{major}.{minor}.{patch}";
        }
    }
}

internal sealed unsafe class VulkanInstance : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<VulkanInstance>();
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

        if (_enableValidation && _vk.TryGetInstanceExtension(_instance, out _debugUtils))
        {
            _debugMessenger = CreateDebugMessenger();
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

    public VulkanPhysicalDeviceInfo[] GetAvailableGpus()
    {
        var devices = EnumeratePhysicalDevices();
        var result = new VulkanPhysicalDeviceInfo[devices.Length];

        for (int i = 0; i < devices.Length; i++)
        {
            result[i] = GetPhysicalDeviceDetails(devices[i]);
        }

        return result;
    }

    public VulkanPhysicalDeviceInfo SelectBestPhysicalDevice()
    {
        var gpus = GetAvailableGpus();

        if (gpus.Length == 0)
        {
            throw new InvalidOperationException("No Vulkan-capable GPU found");
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

        return new VulkanPhysicalDeviceInfo(device, deviceName, deviceType, properties.ApiVersion, memoryInfo);
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
                s_logger.LogWarning("Validation layers requested but not available, continuing without them.");
                validationLayers = Array.Empty<string>();
            }
        }

        var availableExtensions = EnumerateInstanceExtensions();
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
                ApiVersion = Vk.Version12
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
