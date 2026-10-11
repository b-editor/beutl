using Silk.NET.Vulkan;

namespace Beutl.Graphics.Backend.Vulkan;

internal record VulkanPhysicalDeviceInfo(
    PhysicalDevice Device,
    string Name,
    PhysicalDeviceType Type,
    uint ApiVersionInt,
    VulkanMemoryInfo Memory,
    DriverId DriverId)
{
    /// <summary>Gets whether MoltenVK drives this device, which is what puts a Metal device behind it.</summary>
    /// <remarks>
    /// MoltenVK reports the Metal device's own name, so only Apple silicon carries Apple's name: the Intel and AMD
    /// GPUs of an Intel Mac are named after their vendors, and Asahi Linux's own driver names Apple silicon after
    /// Apple too. The driver ID is what identifies MoltenVK.
    /// </remarks>
    public bool IsMoltenVK => DriverId == DriverId.Moltenvk;

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

    /// <summary>
    /// Converts this Vulkan-specific device info to a public <see cref="GraphicsDeviceInfo"/>.
    /// </summary>
    public GraphicsDeviceInfo ToGraphicsDeviceInfo()
    {
        var deviceType = Type switch
        {
            PhysicalDeviceType.IntegratedGpu => GraphicsDeviceType.Integrated,
            PhysicalDeviceType.DiscreteGpu => GraphicsDeviceType.Discrete,
            PhysicalDeviceType.VirtualGpu => GraphicsDeviceType.Virtual,
            PhysicalDeviceType.Cpu => GraphicsDeviceType.Cpu,
            _ => GraphicsDeviceType.Other
        };

        return new GraphicsDeviceInfo(Name, deviceType, ApiVersion, Memory.DeviceLocalMemory / (1024 * 1024))
        {
            IsMoltenVK = IsMoltenVK
        };
    }
}
