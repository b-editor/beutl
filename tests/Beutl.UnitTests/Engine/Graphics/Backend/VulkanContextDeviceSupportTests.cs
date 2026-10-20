using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Silk.NET.Vulkan;
using SkiaSharp;

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
            new VulkanMemoryInfo(0),
            DriverId: default);

        // No instance is passed: the device has to be refused before the context calls into Vulkan at all, so that
        // GraphicsContextFactory falls back to CPU rendering without a half-built context.
        Assert.That(() => new VulkanContext(null!, device), Throws.TypeOf<NotSupportedException>());
    }

    // Skia gives no context, null or a throw, for a device it cannot drive, so the context refuses that device.
    [TestCase(false, typeof(NotSupportedException))]
    [TestCase(true, typeof(InvalidOperationException))]
    public void ADeviceSkiaHasNoContextFor_IsRefused(bool creationThrows, Type expected)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanInstance instance = GraphicsContextFactory.VulkanInstance
            ?? throw new InvalidOperationException("The shared context exists without a Vulkan instance.");
        VulkanPhysicalDeviceInfo? device = instance.EnumeratePhysicalDevices()
            .Select(instance.GetPhysicalDeviceDetails)
            .FirstOrDefault(static info => !info.IsMoltenVK && info.ApiVersionInt >= Vk.Version11);
        if (device is null)
            Assert.Ignore("No Vulkan device outside MoltenVK is available.");

        Func<GRVkBackendContext, GRContext?> createSkiaContext = creationThrows
            ? static _ => throw new InvalidOperationException("Skia failed.")
            : static _ => null;
        Exception? refusal = VulkanTestEnvironment.InvokeOnRenderThread<Exception?>(() =>
        {
            try
            {
                new VulkanContext(instance, device, createSkiaContext).Dispose();
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        });

        Assert.That(refusal, Is.TypeOf(expected));
    }
}
