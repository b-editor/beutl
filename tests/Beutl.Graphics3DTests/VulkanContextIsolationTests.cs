using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Media;
using Silk.NET.Vulkan;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class VulkanContextIsolationTests
{
    private const int Width = 16;
    private const int Height = 8;

    [Test]
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

    [Test]
    public void AResourceFromAnotherContext_IsRejectedByADescriptorUpdate()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using IGraphicsContext foreign = GraphicsContextFactory.CreateContext();
            IShaderCompiler compiler = context.CreateShaderCompiler();
            using IDisposable? compilerLifetime = compiler as IDisposable;
            byte[] vertex = compiler.CompileToSpirv(Graphics3DTestHelpers.FullscreenVertexShader, ShaderStage.Vertex);
            byte[] fragment = compiler.CompileToSpirv(
                Graphics3DTestHelpers.PassthroughFragmentShader, ShaderStage.Fragment);

            using IRenderPass3D pass = context.CreateRenderPass3D([TextureFormat.RGBA8Unorm], null);
            DescriptorBinding[] bindings =
                [new(0, Beutl.Graphics.Backend.DescriptorType.CombinedImageSampler, 1, ShaderStage.Fragment)];
            using IPipeline3D pipeline = context.CreatePipeline3D(
                pass,
                vertex,
                fragment,
                bindings,
                VertexInputDescription.Empty,
                PipelineOptions.Fullscreen);
            using IDescriptorSet descriptors = context.CreateDescriptorSet(
                pipeline,
                [new Beutl.Graphics.Backend.DescriptorPoolSize(
                    Beutl.Graphics.Backend.DescriptorType.CombinedImageSampler,
                    1)]);

            using ITexture2D ownTexture = CreateColorTexture(context);
            using ISampler ownSampler = context.CreateSampler();
            using ITexture2D foreignTexture = CreateColorTexture(foreign);
            using ISampler foreignSampler = foreign.CreateSampler();
            using IBuffer foreignBuffer = foreign.CreateBuffer(
                256,
                BufferUsage.UniformBuffer,
                MemoryProperty.HostVisible | MemoryProperty.HostCoherent);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    () => descriptors.UpdateTexture(0, foreignTexture, ownSampler),
                    Throws.ArgumentException,
                    "a texture from another device has no valid image view here");
                Assert.That(
                    () => descriptors.UpdateTexture(0, ownTexture, foreignSampler),
                    Throws.ArgumentException,
                    "a sampler from another device has no valid handle here");
                Assert.That(
                    () => descriptors.UpdateBuffer(0, foreignBuffer),
                    Throws.ArgumentException,
                    "a buffer from another device has no valid handle here");
                Assert.That(
                    () => descriptors.UpdateTexture(0, ownTexture, ownSampler),
                    Throws.Nothing,
                    "the control: this context's own resources still write");
            }

            context.WaitIdle();
        });
    }

    [Test]
    public void EveryBindEntryPointSkiaRequests_IsIntercepted()
    {
        VulkanContext vulkanContext = GpuTestEnvironment.EnsureVulkanAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            Vk vk = vulkanContext.Vk;
            Device device = vulkanContext.Device;
            foreach (string name in new[] { "vkBindImageMemory", "vkBindImageMemory2" })
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
    public void TheLogicalDevice_EnablesTheShaderFeaturesItAdvertises()
    {
        VulkanContext vulkanContext = GpuTestEnvironment.EnsureVulkanAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            PhysicalDeviceFeatures available = ReadAdvertisedFeatures(vulkanContext);
            TestContext.WriteLine(
                $"advertised int64={available.ShaderInt64} float64={available.ShaderFloat64} "
                + $"cubeArray={available.ImageCubeArray}");
            Assert.Multiple(() =>
            {
                Assert.That(vulkanContext.SupportsShaderInt64, Is.EqualTo((bool)available.ShaderInt64));
                Assert.That(vulkanContext.SupportsShaderFloat64, Is.EqualTo((bool)available.ShaderFloat64));
                Assert.That(
                    vulkanContext.SupportsImageCubeArray,
                    Is.EqualTo((bool)available.ImageCubeArray),
                    "point-light shadows sample a cube array, so the device has to request the feature "
                    + "its own views and shaders rely on");
            });
        });
    }

    [Test]
    public void AFreshArrayTexture_IsReadableInEverySlotBeforeAnythingWritesToIt()
    {
        VulkanContext context = GpuTestEnvironment.EnsureVulkanAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            // Through the context, which picks the usage flags the format supports. Constructing the texture
            // directly would let this fixture ask for a depth attachment backed by a colour format.
            using var array = (VulkanTextureArray)context.CreateTextureArray(
                Width,
                Height,
                4,
                TextureFormat.RGBA8Unorm);

            using (Assert.EnterMultipleScope())
            {
                for (uint layer = 0; layer < 4; layer++)
                {
                    Assert.That(
                        array.GetLayerLayout(layer),
                        Is.EqualTo(ImageLayout.ShaderReadOnlyOptimal),
                        $"layer {layer}");
                }
            }
        });
    }

    [Test]
    public void AFreshCubeArrayTexture_IsReadableInEveryFaceBeforeAnythingWritesToIt()
    {
        VulkanContext context = GpuTestEnvironment.EnsureVulkanAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var cubes = (VulkanTextureCubeArray)context.CreateTextureCubeArray(
                Height,
                2,
                TextureFormat.Depth32Float);

            using (Assert.EnterMultipleScope())
            {
                for (uint cube = 0; cube < 2; cube++)
                {
                    for (int face = 0; face < 6; face++)
                    {
                        Assert.That(
                            cubes.GetLayerLayout(cube * 6 + (uint)face),
                            Is.EqualTo(ImageLayout.ShaderReadOnlyOptimal),
                            $"cube {cube} face {face}");
                    }
                }
            }
        });
    }

    private static unsafe PhysicalDeviceFeatures ReadAdvertisedFeatures(VulkanContext context)
    {
        PhysicalDeviceFeatures features;
        context.Vk.GetPhysicalDeviceFeatures(context.PhysicalDevice, &features);
        return features;
    }

    private static ITexture2D CreateColorTexture(IGraphicsContext context)
        => context.CreateTexture2D(Width, Height, TextureFormat.RGBA8Unorm);
}
