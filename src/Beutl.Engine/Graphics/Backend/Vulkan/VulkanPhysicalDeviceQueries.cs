using System.Runtime.InteropServices;
using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

/// <summary>
/// Physical-device lookups shared by the rendering device, its context and the dedicated presentation device.
/// </summary>
internal static unsafe class VulkanPhysicalDeviceQueries
{
    /// <summary>
    /// Finds a suitable memory type for the given requirements.
    /// </summary>
    public static uint FindMemoryType(
        Vk vk,
        PhysicalDevice physicalDevice,
        uint typeFilter,
        MemoryPropertyFlags properties)
    {
        PhysicalDeviceMemoryProperties memProps;
        vk.GetPhysicalDeviceMemoryProperties(physicalDevice, &memProps);

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

    /// <summary>Finds the first queue family of <paramref name="physicalDevice"/> that supports graphics.</summary>
    public static uint FindGraphicsQueueFamily(Vk vk, PhysicalDevice physicalDevice)
    {
        uint queueFamilyCount = 0;
        vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, null);

        var queueFamilies = new QueueFamilyProperties[queueFamilyCount];
        fixed (QueueFamilyProperties* pQueueFamilies = queueFamilies)
        {
            vk.GetPhysicalDeviceQueueFamilyProperties(physicalDevice, &queueFamilyCount, pQueueFamilies);
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

    /// <summary>Gets the names of the device extensions <paramref name="physicalDevice"/> offers.</summary>
    public static HashSet<string> GetDeviceExtensionNames(Vk vk, PhysicalDevice physicalDevice)
    {
        uint extensionCount = 0;
        vk.EnumerateDeviceExtensionProperties(physicalDevice, (byte*)null, &extensionCount, null);

        var availableExtensions = new ExtensionProperties[extensionCount];
        fixed (ExtensionProperties* pExtensions = availableExtensions)
        {
            vk.EnumerateDeviceExtensionProperties(physicalDevice, (byte*)null, &extensionCount, pExtensions);
        }

        return ToNameSet(availableExtensions);
    }

    /// <summary>Collects the non-empty names of instance or device extension properties.</summary>
    public static HashSet<string> ToNameSet(ExtensionProperties[] extensions)
    {
        var names = new HashSet<string>();
        foreach (var ext in extensions)
        {
            var name = Marshal.PtrToStringAnsi((IntPtr)ext.ExtensionName);
            if (!string.IsNullOrEmpty(name))
                names.Add(name);
        }

        return names;
    }
}
