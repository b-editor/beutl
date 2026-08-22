using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Composite;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Media;
using Silk.NET.Vulkan;

namespace Beutl.Graphics3DTests;

/// <summary>
/// Pins the boundary checks the Vulkan backend owes its callers: a handle means nothing outside the device
/// that made it, and a render pass instance cannot contain another on the same command buffer. Vulkan
/// diagnoses neither, so both have to be rejected before a native call.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class VulkanContextIsolationTests
{
    private const int Width = 16;
    private const int Height = 8;

    [Test]
    [Category("GpuPassFusionGpu")]
    public void ASecondRenderPass_IsRejectedWhileAnotherIsRecording()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using IRenderPass3D outer = context.CreateRenderPass3D([TextureFormat.RGBA8Unorm], null);
            using ITexture2D outerColor = CreateColorTexture(context);
            using IFramebuffer3D outerFramebuffer = context.CreateFramebuffer3D(outer, [outerColor], null);
            using IRenderPass3D inner = context.CreateRenderPass3D([TextureFormat.RGBA8Unorm], null);
            using ITexture2D innerColor = CreateColorTexture(context);
            using IFramebuffer3D innerFramebuffer = context.CreateFramebuffer3D(inner, [innerColor], null);

            outer.Begin(outerFramebuffer, [Colors.Transparent]);
            try
            {
                Assert.That(
                    () => inner.Begin(innerFramebuffer, [Colors.Transparent]),
                    Throws.InvalidOperationException,
                    "Vulkan forbids a render pass instance inside another on the same command buffer.");
            }
            finally
            {
                outer.End();
            }

            Assert.That(
                () => inner.Begin(innerFramebuffer, [Colors.Transparent]),
                Throws.Nothing,
                "The rejected attempt must not leave the batch claimed.");
            inner.End();
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void AFramebufferFromAnotherContext_IsRejected()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using IGraphicsContext foreign = GraphicsContextFactory.CreateContext();
            using IRenderPass3D pass = context.CreateRenderPass3D([TextureFormat.RGBA8Unorm], null);
            using IRenderPass3D foreignPass = foreign.CreateRenderPass3D([TextureFormat.RGBA8Unorm], null);
            using ITexture2D foreignColor = CreateColorTexture(foreign);
            using IFramebuffer3D foreignFramebuffer =
                foreign.CreateFramebuffer3D(foreignPass, [foreignColor], null);

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => context.CreateFramebuffer3D(pass, [foreignColor], null),
                    Throws.ArgumentException,
                    "A texture allocated on another device cannot back this context's framebuffer.");
                Assert.That(
                    () => pass.Begin(foreignFramebuffer, [Colors.Transparent]),
                    Throws.ArgumentException,
                    "A framebuffer from another device cannot be bound here.");
            });
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void ABufferFromAnotherContext_IsRejectedByACopy()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using IGraphicsContext foreign = GraphicsContextFactory.CreateContext();
            using IBuffer local = context.CreateBuffer(
                16,
                BufferUsage.TransferSource | BufferUsage.TransferDestination,
                MemoryProperty.HostVisible | MemoryProperty.HostCoherent);
            using IBuffer other = foreign.CreateBuffer(
                16,
                BufferUsage.TransferSource | BufferUsage.TransferDestination,
                MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

            Assert.Multiple(() =>
            {
                Assert.That(() => context.CopyBuffer(other, local, 16), Throws.ArgumentException);
                Assert.That(() => context.CopyBuffer(local, other, 16), Throws.ArgumentException);
            });
        });
    }

    /// <remarks>
    /// Skia's allocator picks whichever bind entry point the device exposes. Intercepting only the 1.0 form
    /// let a scratch image bound through the core 1.1 or KHR form skip the transparent clear and show
    /// whatever the reused allocation last held.
    /// </remarks>
    [Test]
    [Category("GpuPassFusionGpu")]
    public void EveryBindEntryPointTheDeviceExposes_IsIntercepted()
    {
        VulkanContext vulkanContext = ResolveVulkan(GpuTestEnvironment.EnsureAvailable());
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            Vk vk = vulkanContext.Vk;
            Device device = vulkanContext.Device;
            foreach (string name in new[] { "vkBindImageMemory", "vkBindImageMemory2", "vkBindImageMemory2KHR" })
            {
                nint real = vk.GetDeviceProcAddr(device, name);
                if (real == 0)
                {
                    TestContext.WriteLine($"{name}: not exposed by this device");
                    continue;
                }

                nint resolved = vulkanContext.GetVulkanProcAddress(name, IntPtr.Zero, device.Handle);
                Assert.That(resolved, Is.Not.EqualTo(real), $"{name} must resolve to the initializing proxy.");
                Assert.That(resolved, Is.Not.EqualTo(IntPtr.Zero));
            }
        });
    }

    [Test]
    [Category("GpuPassFusionGpu")]
    public void TheLogicalDevice_EnablesTheSixtyFourBitShaderFeaturesItAdvertises()
    {
        VulkanContext vulkanContext = ResolveVulkan(GpuTestEnvironment.EnsureAvailable());
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            PhysicalDeviceFeatures available = ReadAdvertisedFeatures(vulkanContext);
            TestContext.WriteLine(
                $"advertised int64={available.ShaderInt64} float64={available.ShaderFloat64}");
            Assert.Multiple(() =>
            {
                Assert.That(vulkanContext.SupportsShaderInt64, Is.EqualTo((bool)available.ShaderInt64));
                Assert.That(vulkanContext.SupportsShaderFloat64, Is.EqualTo((bool)available.ShaderFloat64));
            });
        });
    }

    // On macOS the shared context is a CompositeContext pairing a Metal context with the Vulkan one that
    // owns the device; everywhere else it is the Vulkan context itself.
    private static VulkanContext ResolveVulkan(IGraphicsContext context)
        => context switch
        {
            VulkanContext vulkan => vulkan,
            CompositeContext composite => composite.Vulkan,
            _ => throw new InvalidOperationException(
                $"'{context.GetType().Name}' is not backed by a Vulkan context."),
        };

    private static unsafe PhysicalDeviceFeatures ReadAdvertisedFeatures(VulkanContext context)
    {
        PhysicalDeviceFeatures features;
        context.Vk.GetPhysicalDeviceFeatures(context.PhysicalDevice, &features);
        return features;
    }

    private static ITexture2D CreateColorTexture(IGraphicsContext context)
        => context.CreateTexture2D(Width, Height, TextureFormat.RGBA8Unorm);
}
