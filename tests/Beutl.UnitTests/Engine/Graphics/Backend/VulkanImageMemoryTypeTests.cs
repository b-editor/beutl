using Beutl.Graphics.Backend.Vulkan;
using Silk.NET.Vulkan;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

[TestFixture]
public sealed class VulkanImageMemoryTypeTests
{
    [TestCase(128, 0)]
    [TestCase(512, 2)]
    [TestCase(1024, 1)]
    public void ImageAllocation_PrefersCompatibleDeviceLocalHeapsThatCanFit(int size, int expected)
    {
        PhysicalDeviceMemoryProperties properties = CreateMemoryProperties();
        var requirements = new MemoryRequirements { Size = (ulong)size, MemoryTypeBits = 0b111 };

        Assert.That(VulkanPhysicalDeviceQueries.FindImageMemoryType(in properties, requirements), Is.EqualTo((uint)expected));
    }

    [Test]
    public void ImageAllocation_RejectsOversizeWhenOnlyTheIncompatibleHeapCanFit()
    {
        PhysicalDeviceMemoryProperties properties = CreateMemoryProperties();
        var requirements = new MemoryRequirements { Size = 1024, MemoryTypeBits = 0b101 };

        Assert.That(() => VulkanPhysicalDeviceQueries.FindImageMemoryType(in properties, requirements),
            Throws.InvalidOperationException.With.Message.Contains("1024 bytes"));
    }

    private static PhysicalDeviceMemoryProperties CreateMemoryProperties()
    {
        var properties = new PhysicalDeviceMemoryProperties { MemoryTypeCount = 3, MemoryHeapCount = 3 };
        properties.MemoryHeaps[0] = new MemoryHeap { Size = 256, Flags = MemoryHeapFlags.DeviceLocalBit };
        properties.MemoryHeaps[1] = new MemoryHeap { Size = 2048 };
        properties.MemoryHeaps[2] = new MemoryHeap { Size = 768, Flags = MemoryHeapFlags.DeviceLocalBit };
        properties.MemoryTypes[0] = new MemoryType { HeapIndex = 0, PropertyFlags = MemoryPropertyFlags.DeviceLocalBit };
        properties.MemoryTypes[1] = new MemoryType { HeapIndex = 1, PropertyFlags = MemoryPropertyFlags.HostVisibleBit };
        properties.MemoryTypes[2] = new MemoryType { HeapIndex = 2, PropertyFlags = MemoryPropertyFlags.DeviceLocalBit };
        return properties;
    }
}
