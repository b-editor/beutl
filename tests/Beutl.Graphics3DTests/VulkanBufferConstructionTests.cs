using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Composite;
using Beutl.Graphics.Backend.Vulkan;
using Silk.NET.Vulkan;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class VulkanBufferConstructionTests
{
    [Test]
    [Category("GpuPassFusionGpu")]
    public void ABufferWithNoCompatibleMemoryType_LeavesNothingForDeviceTeardown()
    {
        GpuTestEnvironment.EnsureAvailable();
        TestContext.WriteLine($"GPU device: {GraphicsContextFactory.GetSelectedDevice()}");
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using IGraphicsContext context = GraphicsContextFactory.CreateContext();
            VulkanContext vulkan = context switch
            {
                VulkanContext value => value,
                CompositeContext composite => composite.Vulkan,
                _ => throw new InvalidOperationException("The graphics context has no Vulkan backend."),
            };

            // Host-visible and lazily allocated memory cannot coexist. Confirm the device's answer
            // before the constructor, so the refusal is caused by memory selection, not allocation.
            Assert.That(
                () => vulkan.FindMemoryType(uint.MaxValue,
                    MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.LazilyAllocatedBit),
                Throws.InvalidOperationException.With.Message.EqualTo("Failed to find suitable memory type"));
            Assert.That(
                () => context.CreateBuffer(16, BufferUsage.TransferDestination,
                    MemoryProperty.HostVisible | MemoryProperty.LazilyAllocated),
                Throws.InvalidOperationException.With.Message.EqualTo("Failed to find suitable memory type"));

            // Disposing this private device lets the validation gate detect an unbound VkBuffer
            // stranded by the constructor's exception. The shared rendering device remains available.
        });
    }
}
