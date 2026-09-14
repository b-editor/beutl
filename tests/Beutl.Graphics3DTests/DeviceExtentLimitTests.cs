using Beutl.Graphics.Backend;

namespace Beutl.Graphics3DTests;

/// <summary>
/// The live context refuses an extent past its own limits before the driver sees it. SwiftShader would
/// otherwise build the image and answer success; MoltenVK would abort the process, so a failure here can
/// present as a crashed test host rather than a failed assertion.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class DeviceExtentLimitTests
{
    [Test]
    public void TheDevice_ReportsACubeFaceLimit()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();

        Assert.That(context.MaxCubeFaceDimension, Is.GreaterThan(0),
            "a cube map is bounded by its own limit, so the device has to report one");
    }

    [Test]
    public void ATexture2DPastTheImageLimit_IsRefusedBeforeTheDriver()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            int limit = ImageLimitOf(context);
            Assume.That(limit, Is.GreaterThan(0));

            InvalidOperationException? refusal = Assert.Throws<InvalidOperationException>(
                () => context.CreateTexture2D(limit + 1, 1, TextureFormat.RGBA8Unorm)?.Dispose());

            Assert.That(refusal!.Message, Does.Contain(limit.ToString()));
        });
    }

    [Test]
    public void ASampledTextureBetweenTheAttachmentAndImageLimits_IsStillMade()
    {
        // A material map is only ever sampled, and a device may sample an image wider than it can attach
        // (SwiftShader: 16384 against 8192), so the context must not apply the attachment limit to it.
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        int attachment = context.MaxAttachmentDimension;
        // Outside the render-thread invoke: an inconclusive result raised inside it is reported as a failure.
        Assume.That(ImageLimitOf(context), Is.GreaterThan(attachment),
            "this device attaches everything it can sample, so the two limits cannot be told apart");

        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using ITexture2D texture = context.CreateTexture2D(attachment + 1, 1, TextureFormat.RGBA8Unorm);

            Assert.That(texture, Is.Not.Null);
            context.WaitIdle();
        });
    }

    private static int ImageLimitOf(IGraphicsContext context) => context switch
    {
        Beutl.Graphics.Backend.Vulkan.VulkanContext vulkan => vulkan.MaxImageDimension2D,
        Beutl.Graphics.Backend.Composite.CompositeContext composite => composite.MaxImageDimension2D,
        _ => throw new InvalidOperationException($"Unexpected context type {context.GetType()}"),
    };

    [Test]
    public void ACubePastTheCubeLimit_IsRefusedBeforeTheDriver()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            int limit = context.MaxCubeFaceDimension;
            Assume.That(limit, Is.GreaterThan(0));

            InvalidOperationException? refusal = Assert.Throws<InvalidOperationException>(
                () => context.CreateTextureCube(limit + 1, TextureFormat.Depth32Float)?.Dispose());

            Assert.That(refusal!.Message, Does.Contain(limit.ToString()));
        });
    }
}
