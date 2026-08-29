using System.Reflection;

using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Composite;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Pixel;

using Silk.NET.Vulkan;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

public class SkiaVulkanImageInitializationTests
{
    [Test]
    [Category("GpuPassFusionGpu")]
    public void BackendClear_CompletesBeforeSkiaPartiallyOverwritesTarget()
    {
        IGraphicsContext context = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            context.WaitIdle();
            using RenderTarget target = RenderTarget.Create(4, 4)
                ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
            var events = new List<VulkanCommandPoolEvent>();

            using (VulkanCommandPool.Observe(events.Add))
            {
                target.BeginDraw();
                using var paint = new SKPaint { Color = SKColors.Red };
                target.Value.Canvas.DrawRect(SKRect.Create(0, 0, 2, 2), paint);
            }

            using Bitmap snapshot = target.Snapshot();
            RgbaF16 untouched = snapshot.GetPixelSpan<RgbaF16>()[15];
            Assert.Multiple(() =>
            {
                Assert.That(
                    events.Count(static item => item == VulkanCommandPoolEvent.Submission),
                    Is.EqualTo(1),
                    "The backend clear must be submitted before Skia records a partial overwrite.");
                // Queue ordering carries the clear only where Skia submits to the same Vulkan queue.
                // On the composite backend Skia draws through Metal, which shares no semaphore with
                // Beutl's Vulkan submissions, so the hand-off has to complete on the CPU instead.
                Assert.That(
                    events.Count(static item => item == VulkanCommandPoolEvent.FenceWait),
                    context.Backend == GraphicsBackend.Vulkan ? Is.Zero : Is.EqualTo(1),
                    "The backend clear must reach Skia by queue order, or by a completion wait when the "
                    + "two APIs share no queue.");
                Assert.That(untouched, Is.EqualTo(default(RgbaF16)));
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    [Category(TestCategories.KnownVulkanSkiaLayoutInterop)]
    public void NewRenderTarget_SubmitsInitializationBeforeUntouchedSnapshot()
    {
        IGraphicsContext context = VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            context.WaitIdle();
            using RenderTarget target = RenderTarget.Create(4, 4)
                ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
            var events = new List<VulkanCommandPoolEvent>();

            Bitmap snapshot;
            using (VulkanCommandPool.Observe(events.Add))
                snapshot = target.Snapshot();

            using (snapshot)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(
                        events.Count(static item => item == VulkanCommandPoolEvent.Submission),
                        Is.EqualTo(1),
                        "The recorded allocation clear must be submitted before an untouched snapshot.");
                    Assert.That(
                        events.Count(static item => item == VulkanCommandPoolEvent.FenceWait),
                        Is.EqualTo(1));
                    Assert.That(
                        snapshot.GetPixelSpan<RgbaF16>().ToArray(),
                        Is.All.EqualTo(default(RgbaF16)));
                });
            }
        });
    }

    [Test]
    [NonParallelizable]
    public void Context_InterceptsSkiaImageAllocationFunctions()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // The shared context is a CompositeContext wherever Skia runs on another API, so the
            // Vulkan context that owns the hook has to be reached through it.
            IGraphicsContext shared = GraphicsContextFactory.GetOrCreateShared()!;
            VulkanContext context = shared as VulkanContext
                ?? ((CompositeContext)shared).Vulkan;
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
