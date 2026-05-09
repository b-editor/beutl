using Beutl.Graphics.Rendering;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

[NonParallelizable]
public class RenderTargetVulkanTests
{
    [Test]
    public void Create_OnRenderThread_UsesGraphicsContext()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(64, 64);

            Assert.That(target, Is.Not.Null);
            Assert.That(target!.Width, Is.EqualTo(64));
            Assert.That(target.Height, Is.EqualTo(64));
            Assert.That(target.IsDisposed, Is.False);
        });
    }

    [Test]
    public void Create_Snapshot_ProducesSizedBitmap()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(16, 8);
            Assume.That(target, Is.Not.Null);

            using var bitmap = target!.Snapshot();

            Assert.That(bitmap.Width, Is.EqualTo(16));
            Assert.That(bitmap.Height, Is.EqualTo(8));
        });
    }

    [Test]
    public void ShallowCopy_SharesUnderlyingResource()
    {
        VulkanTestEnvironment.EnsureAvailable();

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var target = RenderTarget.Create(32, 32);
            Assume.That(target, Is.Not.Null);

            using var copy = target!.ShallowCopy();

            Assert.That(copy, Is.Not.SameAs(target));
            Assert.That(copy.Width, Is.EqualTo(target.Width));
            Assert.That(copy.Height, Is.EqualTo(target.Height));
            Assert.That(copy.IsDisposed, Is.False);
        });
    }
}
