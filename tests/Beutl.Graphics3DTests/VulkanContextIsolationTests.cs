using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Composite;
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

    [Test]
    [Category("GpuPassFusionGpu")]
    public void AResourceFromAnotherContext_IsRejectedByADescriptorUpdate()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using IGraphicsContext foreign = GraphicsContextFactory.CreateContext();
            IShaderCompiler compiler = context.CreateShaderCompiler();
            byte[] vertex = compiler.CompileToSpirv(SampledVertexShader, ShaderStage.Vertex);
            byte[] fragment = compiler.CompileToSpirv(SampledFragmentShader, ShaderStage.Fragment);

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
    public void TheLogicalDevice_EnablesTheShaderFeaturesItAdvertises()
    {
        VulkanContext vulkanContext = ResolveVulkan(GpuTestEnvironment.EnsureAvailable());
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
    [Category("GpuPassFusionGpu")]
    public void AFreshArrayTexture_IsReadableInEverySlotBeforeAnythingWritesToIt()
    {
        VulkanContext context = ResolveVulkan(GpuTestEnvironment.EnsureAvailable());
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
    [Category("GpuPassFusionGpu")]
    public void AFreshCubeArrayTexture_IsReadableInEveryFaceBeforeAnythingWritesToIt()
    {
        VulkanContext context = ResolveVulkan(GpuTestEnvironment.EnsureAvailable());
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
                            cubes.GetFaceLayout(cube, face),
                            Is.EqualTo(ImageLayout.ShaderReadOnlyOptimal),
                            $"cube {cube} face {face}");
                    }
                }
            }
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

    private const string SampledVertexShader = """
        #version 450

        layout(location = 0) out vec2 fragCoord;

        void main() {
            vec2 positions[3] = vec2[](vec2(-1.0, -1.0), vec2(3.0, -1.0), vec2(-1.0, 3.0));
            vec2 uvs[3] = vec2[](vec2(0.0, 0.0), vec2(2.0, 0.0), vec2(0.0, 2.0));
            gl_Position = vec4(positions[gl_VertexIndex], 0.0, 1.0);
            fragCoord = uvs[gl_VertexIndex];
        }
        """;

    private const string SampledFragmentShader = """
        #version 450

        layout(location = 0) in vec2 fragCoord;
        layout(location = 0) out vec4 outColor;
        layout(binding = 0) uniform sampler2D sourceTexture;

        void main() {
            outColor = texture(sourceTexture, fragCoord);
        }
        """;
}
