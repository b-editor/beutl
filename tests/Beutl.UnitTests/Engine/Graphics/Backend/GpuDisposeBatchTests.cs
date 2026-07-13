using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

/// <summary>
/// A <see cref="GpuDisposeBatch"/> drain request for an already-destroyed context (null GRContext) issues no flush
/// and must not consume the batch's single drain: consuming it would suppress the drain of the batch's remaining
/// live-context textures, re-opening the teardown use-after-free the drain exists to prevent.
/// </summary>
[NonParallelizable]
[TestFixture]
public class GpuDisposeBatchTests
{
    [Test]
    public void DrainBeforeDestroy_NullContext_DoesNotConsumeTheBatchDrain()
    {
        GpuDisposeBatch.ResetFlushCountForTest();
        using (GpuDisposeBatch.Begin())
        {
            GpuDisposeBatch.DrainBeforeDestroy(null);

            Assert.Multiple(() =>
            {
                Assert.That(GpuDisposeBatch.FlushCount, Is.Zero,
                    "a destroyed-context drain request has nothing to flush");
                Assert.That(GpuDisposeBatch.DrainConsumedForTest, Is.False,
                    "the batch's single drain stays available for a live-context texture destroyed later in the batch");
            });
        }
    }

    [Test]
    public void VulkanTextureDispose_DrainThrows_StillReleasesNativeHandles()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var context = GraphicsContextFactory.SharedContext
                ?? throw new InvalidOperationException("A graphics context is required.");
            var texture = (VulkanTexture2D)context.CreateTexture2D(16, 16, TextureFormat.RGBA16Float);

            GpuDisposeBatch.SetDrainFailureForTest(
                static () => throw new InvalidOperationException("simulated context-loss drain failure"));
            try
            {
                Assert.DoesNotThrow(texture.Dispose);
                Assert.That(texture.NativeHandlesReleasedForTest, Is.True,
                    "drain failure must not skip image-view, image, and memory teardown");
            }
            finally
            {
                GpuDisposeBatch.SetDrainFailureForTest(null);
                texture.Dispose();
            }
        });
    }
}
