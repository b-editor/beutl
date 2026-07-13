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

    [Test]
    public void DrainFailureInjection_DoesNotLeakAcrossThreads()
    {
        GpuDisposeBatch.SetDrainFailureForTest(
            static () => throw new InvalidOperationException("thread-local injected failure"));
        Exception? crossThreadFailure = null;
        try
        {
            var thread = new Thread(() =>
            {
                try
                {
                    GpuDisposeBatch.DrainBeforeDestroy(null);
                }
                catch (Exception ex)
                {
                    crossThreadFailure = ex;
                }
            });
            thread.Start();
            thread.Join();
        }
        finally
        {
            GpuDisposeBatch.SetDrainFailureForTest(null);
        }

        Assert.That(crossThreadFailure, Is.Null,
            "a failure seam installed on one test thread must not affect concurrent render threads");
    }
}
