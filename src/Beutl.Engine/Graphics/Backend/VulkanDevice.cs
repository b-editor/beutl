using System.Runtime.InteropServices;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend;

internal sealed unsafe class VulkanDevice : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<VulkanDevice>();
    private readonly Vk _vk;
    private readonly Instance _instance;
    private readonly PhysicalDevice _physicalDevice;
    private readonly Device _device;
    private readonly Queue _graphicsQueue;
    private readonly uint _graphicsQueueFamilyIndex;
    private readonly string[] _enabledExtensions;
    private bool _disposed;

    public VulkanDevice(Vk vk, Instance instance, PhysicalDevice physicalDevice)
    {
        _vk = vk;
        _instance = instance;
        _physicalDevice = physicalDevice;

        _graphicsQueueFamilyIndex = FindGraphicsQueueFamily();
        _enabledExtensions = GetRequiredDeviceExtensions();
        _device = CreateDevice(_enabledExtensions);

        _vk.GetDeviceQueue(_device, _graphicsQueueFamilyIndex, 0, out _graphicsQueue);

        PhysicalDeviceProperties properties;
        _vk.GetPhysicalDeviceProperties(_physicalDevice, &properties);
        var deviceName = Marshal.PtrToStringAnsi((IntPtr)properties.DeviceName);
        s_logger.LogInformation("Using GPU: {DeviceName}", deviceName);
    }

    public Vk Vk => _vk;

    public Instance Instance => _instance;

    public PhysicalDevice PhysicalDevice => _physicalDevice;

    public Device Device => _device;

    public Queue GraphicsQueue => _graphicsQueue;

    public uint GraphicsQueueFamilyIndex => _graphicsQueueFamilyIndex;

    public string[] EnabledExtensions => _enabledExtensions;


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

    private string[] GetRequiredDeviceExtensions()
    {
        var extensions = new List<string>();

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

        if (availableNames.Contains("VK_KHR_swapchain"))
            extensions.Add("VK_KHR_swapchain");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (availableNames.Contains("VK_KHR_portability_subset"))
                extensions.Add("VK_KHR_portability_subset");

            if (availableNames.Contains("VK_EXT_metal_objects"))
                extensions.Add("VK_EXT_metal_objects");
        }

        return extensions.ToArray();
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

    public void WaitIdle()
    {
        _vk.DeviceWaitIdle(_device);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _vk.DestroyDevice(_device, null);
    }
}
