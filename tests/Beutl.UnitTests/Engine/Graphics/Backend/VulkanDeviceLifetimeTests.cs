using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Composite;
using Beutl.Graphics.Backend.Vulkan;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

/// <summary>
/// Covers what a command pool does with a deferred release once its logical device is gone.
/// </summary>
/// <remarks>
/// These build a private <see cref="VulkanDevice"/> on the shared context's instance and physical
/// device, so destroying it leaves the shared context untouched.
/// </remarks>
[TestFixture]
[NonParallelizable]
public class VulkanDeviceLifetimeTests
{
    [Test]
    [Category("GpuPassFusionGpu")]
    public void DeferRelease_AfterDeviceDestroyed_DropsTheRelease()
    {
        VulkanContext shared = RequireVulkanContext();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var device = new VulkanDevice(shared.Vk, shared.Instance, shared.PhysicalDevice);
            var pool = new VulkanCommandPool(device);

            // Teardown order matches VulkanContext.Dispose: the pool first, then the device.
            pool.Dispose();
            device.Dispose();

            bool released = false;
            pool.DeferRelease(() => released = true);

            Assert.That(released, Is.False,
                "A release arriving after the logical device was destroyed must be dropped. Its Vulkan "
                + "objects died with the device, so running it would issue vkDestroy* calls against a "
                + "dangling VkDevice.");
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void DeferRelease_AfterPoolDisposedWhileDeviceAlive_StillRunsTheRelease()
    {
        VulkanContext shared = RequireVulkanContext();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var device = new VulkanDevice(shared.Vk, shared.Instance, shared.PhysicalDevice);
            var pool = new VulkanCommandPool(device);

            pool.Dispose();

            try
            {
                bool released = false;
                pool.DeferRelease(() => released = true);

                Assert.That(released, Is.True,
                    "The device outlives the pool during context teardown, so a release arriving in that "
                    + "window still owns live Vulkan objects and must run.");
            }
            finally
            {
                device.Dispose();
            }
        });
    }

    private static VulkanContext RequireVulkanContext()
    {
        IGraphicsContext context = VulkanTestEnvironment.EnsureAvailable();
        return context switch
        {
            VulkanContext vulkan => vulkan,
            CompositeContext composite => composite.Vulkan,
            _ => throw new InvalidOperationException("The shared graphics context has no Vulkan backend."),
        };
    }
}
