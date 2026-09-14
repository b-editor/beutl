using Beutl.Graphics.Backend.Vulkan;
using Silk.NET.Vulkan;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

public class VulkanContextDeviceSupportTests
{
    [Test]
    public void AVulkan10Device_IsRefusedBeforeAnythingIsCreatedOnIt()
    {
        var device = new VulkanPhysicalDeviceInfo(
            default,
            "Legacy GPU",
            PhysicalDeviceType.DiscreteGpu,
            (uint)Vk.Version10,
            new VulkanMemoryInfo(0, 0));

        // No instance is passed: the device has to be refused before the context calls into Vulkan at all, so that
        // GraphicsContextFactory falls back to CPU rendering without a half-built context.
        Assert.That(() => new VulkanContext(null!, device), Throws.TypeOf<NotSupportedException>());
    }
}
