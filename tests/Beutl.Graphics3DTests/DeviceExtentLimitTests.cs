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
    public void ATexture2DPastTheAttachmentLimit_IsRefusedBeforeTheDriver()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            int limit = context.MaxAttachmentDimension;
            Assume.That(limit, Is.GreaterThan(0));

            InvalidOperationException? refusal = Assert.Throws<InvalidOperationException>(
                () => context.CreateTexture2D(limit + 1, 1, TextureFormat.RGBA8Unorm)?.Dispose());

            Assert.That(refusal!.Message, Does.Contain(limit.ToString()));
        });
    }

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
