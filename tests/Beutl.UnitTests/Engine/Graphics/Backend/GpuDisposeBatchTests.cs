using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering;

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
    public void DrainBeforeDestroy_FailedFlush_DoesNotConsumeBatchAndCanRetry()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var context = GraphicsContextFactory.SharedContext
                ?? throw new InvalidOperationException("A graphics context is required.");
            var injected = new InvalidOperationException("simulated batch flush failure");
            GpuDisposeBatch.ResetFlushCountForTest();

            using (GpuDisposeBatch.Begin())
            {
                GpuDisposeBatch.SetDrainFailureForTest(() => throw injected);
                InvalidOperationException? actual;
                try
                {
                    actual = Assert.Throws<InvalidOperationException>(
                        () => GpuDisposeBatch.DrainBeforeDestroy(context.SkiaContext));
                }
                finally
                {
                    GpuDisposeBatch.SetDrainFailureForTest(null);
                }

                Assert.Multiple(() =>
                {
                    Assert.That(actual, Is.SameAs(injected));
                    Assert.That(GpuDisposeBatch.FlushCount, Is.EqualTo(1));
                    Assert.That(GpuDisposeBatch.DrainConsumedForTest, Is.False,
                        "a failed flush must leave the batch drain available for a later texture");
                });

                GpuDisposeBatch.DrainBeforeDestroy(context.SkiaContext);
                Assert.Multiple(() =>
                {
                    Assert.That(GpuDisposeBatch.FlushCount, Is.EqualTo(2),
                        "the next live texture must retry the drain");
                    Assert.That(GpuDisposeBatch.DrainConsumedForTest, Is.True);
                });
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

    [Test]
    public void Batch_DrainsEachDistinctContextExactlyOnce()
    {
        var firstContext = new object();
        var secondContext = new object();
        int firstDrains = 0;
        int secondDrains = 0;
        GpuDisposeBatch.ResetFlushCountForTest();

        using (GpuDisposeBatch.Begin())
        {
            GpuDisposeBatch.DrainBeforeDestroyForTest(firstContext, () => firstDrains++);
            GpuDisposeBatch.DrainBeforeDestroyForTest(firstContext, () => firstDrains++);
            GpuDisposeBatch.DrainBeforeDestroyForTest(secondContext, () => secondDrains++);
            GpuDisposeBatch.DrainBeforeDestroyForTest(secondContext, () => secondDrains++);
        }

        Assert.Multiple(() =>
        {
            Assert.That(firstDrains, Is.EqualTo(1));
            Assert.That(secondDrains, Is.EqualTo(1));
            Assert.That(GpuDisposeBatch.FlushCount, Is.EqualTo(2),
                "a mixed-context batch must drain once per context, not once for the whole batch");
        });
    }

    [Test]
    public void VulkanTextureDispose_FromWorkerThread_ReleasesHandlesOnRenderThread()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTexture2D texture = VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var context = GraphicsContextFactory.SharedContext
                ?? throw new InvalidOperationException("A graphics context is required.");
            return (VulkanTexture2D)context.CreateTexture2D(16, 16, TextureFormat.RGBA16Float);
        });
        using var renderThreadEntered = new ManualResetEventSlim();
        using var releaseRenderThread = new ManualResetEventSlim();

        try
        {
            RenderThread.Dispatcher.Dispatch(() =>
            {
                renderThreadEntered.Set();
                releaseRenderThread.Wait();
            });
            Assert.That(renderThreadEntered.Wait(TimeSpan.FromSeconds(10)), Is.True,
                "the render-thread blocker must start before the worker disposes the texture");

            Task.Run(texture.Dispose).GetAwaiter().GetResult();

            Assert.That(texture.NativeHandlesReleasedForTest, Is.False,
                "worker disposal must enqueue native teardown instead of draining and destroying handles inline");

            releaseRenderThread.Set();
            RenderThread.Dispatcher.Invoke(static () => { });
            Assert.That(texture.NativeHandlesReleasedForTest, Is.True);
        }
        finally
        {
            releaseRenderThread.Set();
            RenderThread.Dispatcher.Invoke(texture.Dispose);
        }
    }
}
