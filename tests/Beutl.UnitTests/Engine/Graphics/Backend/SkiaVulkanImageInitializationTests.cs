using System.Reflection;

using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;

using Silk.NET.Vulkan;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

public class SkiaVulkanImageInitializationTests
{
    [Test]
    [NonParallelizable]
    public void Context_InterceptsSkiaImageAllocationFunctions()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var context = (VulkanContext)GraphicsContextFactory.GetOrCreateShared()!;
            MethodInfo getProcedureAddress = typeof(VulkanContext).GetMethod(
                "GetVulkanProcAddress",
                BindingFlags.Instance | BindingFlags.NonPublic)!;

            foreach (string name in new[] { "vkCreateImage", "vkBindImageMemory", "vkDestroyImage" })
            {
                IntPtr native = context.Vk.GetDeviceProcAddr(context.Device, name);
                var intercepted = (IntPtr)getProcedureAddress.Invoke(
                    context,
                    [name, context.Instance.Handle, context.Device.Handle])!;
                Assert.That(intercepted, Is.Not.EqualTo(native), $"{name} must pass through the initializer.");
            }
        });
    }

    [Test]
    public void PrepareCreateInfo_MakesColorAttachmentsClearable()
    {
        var createInfo = new ImageCreateInfo
        {
            InitialLayout = ImageLayout.Undefined,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
            MipLevels = 4,
            ArrayLayers = 3,
        };

        ImageCreateInfo prepared = VulkanContext.PrepareSkiaImageCreateInfo(createInfo);

        Assert.That((prepared.Usage & ImageUsageFlags.TransferDstBit) != 0, Is.True);
        Assert.That(VulkanContext.RequiresTransparentInitialization(prepared), Is.True);
        ImageSubresourceRange range = VulkanContext.CreateInitializationRange(prepared);
        Assert.Multiple(() =>
        {
            Assert.That(range.AspectMask, Is.EqualTo(ImageAspectFlags.ColorBit));
            Assert.That(range.BaseMipLevel, Is.Zero);
            Assert.That(range.LevelCount, Is.EqualTo(4));
            Assert.That(range.BaseArrayLayer, Is.Zero);
            Assert.That(range.LayerCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void PrepareCreateInfo_DoesNotChangeNonColorImages()
    {
        var createInfo = new ImageCreateInfo
        {
            InitialLayout = ImageLayout.Undefined,
            Usage = ImageUsageFlags.DepthStencilAttachmentBit,
            MipLevels = 1,
            ArrayLayers = 1,
        };

        ImageCreateInfo prepared = VulkanContext.PrepareSkiaImageCreateInfo(createInfo);

        Assert.That(prepared.Usage, Is.EqualTo(createInfo.Usage));
        Assert.That(VulkanContext.RequiresTransparentInitialization(prepared), Is.False);
    }
}
