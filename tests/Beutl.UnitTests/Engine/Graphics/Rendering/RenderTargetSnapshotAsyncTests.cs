using System.Collections.Immutable;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Threading;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// RenderTarget.SnapshotAsync queues a readback behind the rendering submitted so far and completes once the GPU
// context reports it, instead of making the render thread wait the way Snapshot does.
[NonParallelizable]
[TestFixture]
public class RenderTargetSnapshotAsyncTests
{
    [Test]
    public async Task SnapshotAsync_ReadsTheSamePixelsAsSnapshot()
    {
        VulkanTestEnvironment.EnsureAvailable();
        (RenderTarget target, Bitmap expected, Task<Bitmap> pending) = VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            RenderTarget target = CreateGpuTarget(64, 48);
            using (var canvas = new ImmediateCanvas(target, RenderIntent.Preview, 1f))
            {
                canvas.Clear(Colors.Black);
                canvas.DrawRectangle(new Rect(16, 12, 32, 24), Brushes.Resource.White, null);
            }

            return (target, target.Snapshot(), target.SnapshotAsync());
        });

        try
        {
            // Awaited off the render thread, so only the render thread's polling can complete it.
            using Bitmap actual = await pending;
            Assert.That(actual.GetPixelSpan().SequenceEqual(expected.GetPixelSpan()), Is.True);
        }
        finally
        {
            expected.Dispose();
            VulkanTestEnvironment.InvokeOnRenderThread(target.Dispose);
        }
    }

    [Test]
    public async Task SnapshotAsync_ReturnsWhatTheSurfaceHeldWhenRequested()
    {
        VulkanTestEnvironment.EnsureAvailable();
        (RenderTarget target, Task<Bitmap> pending) = VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            RenderTarget target = CreateGpuTarget(16, 16);
            target.Value.Canvas.Clear(SKColors.White);
            Task<Bitmap> pending = target.SnapshotAsync();
            // Drawn after the request, while the GPU may still be working on it.
            target.Value.Canvas.Clear(SKColors.Black);
            return (target, pending);
        });

        try
        {
            using Bitmap requested = await pending;
            using Bitmap current = VulkanTestEnvironment.InvokeOnRenderThread(target.Snapshot);
            Assert.Multiple(() =>
            {
                Assert.That(ReadRed(requested, 8, 8), Is.EqualTo(1f).Within(1e-3f), "the read must keep the white it was queued behind");
                Assert.That(ReadRed(current, 8, 8), Is.EqualTo(0f).Within(1e-3f), "the target itself must hold the later black");
            });
        }
        finally
        {
            VulkanTestEnvironment.InvokeOnRenderThread(target.Dispose);
        }
    }

    [Test]
    public async Task SnapshotAsync_SubmitsWithoutWaitingForTheGpu()
    {
        VulkanTestEnvironment.EnsureAvailable();
        var flushes = new List<ImmediateCanvasFlushKind>();
        (RenderTarget target, Task<Bitmap> pending) = VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            GpuResourceReclaimQueue.FlushAndDrain();
            RenderTarget target = CreateGpuTarget(8, 8);
            target.Value.Canvas.Clear(SKColors.CornflowerBlue);
            Task<Bitmap> pending;
            using (ImmediateCanvas.ObserveFlushes(flushes.Add))
            {
                pending = target.SnapshotAsync();
            }

            return (target, pending);
        });

        try
        {
            using Bitmap snapshot = await pending;
            Assert.That(flushes,
                Is.EqualTo(new[] { ImmediateCanvasFlushKind.PrepareForSamplingSubmit }),
                "An asynchronous readback must submit the surface without waiting for the GPU to finish it.");
        }
        finally
        {
            VulkanTestEnvironment.InvokeOnRenderThread(target.Dispose);
        }
    }

    [Test]
    public async Task SnapshotAsync_LeavesDeferredResourcesForAFlushThatSynchronizes()
    {
        VulkanTestEnvironment.EnsureAvailable();
        var flushes = new List<ImmediateCanvasFlushKind>();
        var deferred = new DisposalProbe();
        (RenderTarget target, Task<Bitmap> pending, bool accepted, bool disposedByRequest) =
            VulkanTestEnvironment.InvokeOnRenderThread(() =>
            {
                GpuResourceReclaimQueue.FlushAndDrain();
                RenderTarget target = CreateGpuTarget(8, 8);
                target.Value.Canvas.Clear(SKColors.CornflowerBlue);
                bool accepted = GpuResourceReclaimQueue.TryDefer(deferred, 0);
                Task<Bitmap> pending;
                using (ImmediateCanvas.ObserveFlushes(flushes.Add))
                {
                    pending = target.SnapshotAsync();
                }

                return (target, pending, accepted, deferred.IsDisposed);
            });

        try
        {
            using Bitmap snapshot = await pending;
            Assert.Multiple(() =>
            {
                Assert.That(accepted, Is.True);
                Assert.That(disposedByRequest, Is.False, "Reclaiming the queue would have waited for the GPU.");
                Assert.That(flushes, Is.EqualTo(new[] { ImmediateCanvasFlushKind.PrepareForSamplingSubmit }));
            });
        }
        finally
        {
            VulkanTestEnvironment.InvokeOnRenderThread(() =>
            {
                GpuResourceReclaimQueue.FlushAndDrain();
                target.Dispose();
            });
        }
    }

    [Test]
    public async Task SurfaceReadback_FailsWhenItsDispatcherShutsDownBeforeTheReadCompletes()
    {
        Dispatcher dispatcher = Dispatcher.Spawn();
        var destination = new Bitmap(4, 4, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);
        var readback = new RenderTarget.SurfaceReadback(destination);
        var polled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // The context never reports the read finished, so only the shutdown can end it.
        try
        {
            readback.PollUntilComplete(dispatcher, () => polled.TrySetResult(), () => false);
            await polled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            dispatcher.Shutdown();
        }

        Assert.That(
            async () => await readback.Completion.WaitAsync(TimeSpan.FromSeconds(5)),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(destination.IsDisposed, Is.True);
    }

    [Test]
    public void SnapshotAsync_OnACpuSurface_HasCompletedWhenItReturns()
    {
        // Off the render thread, RenderTarget.Create rasters on the CPU, where Skia reads synchronously.
        using RenderTarget target = RenderTarget.Create(16, 16)
                                    ?? throw new InvalidOperationException("Could not create a raster render target.");
        target.Value.Canvas.Clear(SKColors.White);

        Task<Bitmap> pending = target.SnapshotAsync();

        Assert.That(pending.IsCompletedSuccessfully, Is.True);
        using Bitmap snapshot = pending.Result;
        Assert.That(ReadRed(snapshot, 8, 8), Is.EqualTo(1f).Within(1e-3f));
    }

    [Test]
    public async Task SnapshotAsync_OnAGpuTargetNoDispatcherOwns_HasCompletedWhenItReturns()
    {
        Beutl.Graphics.Backend.IGraphicsContext graphics = VulkanTestEnvironment.EnsureAvailable();
        SKSurface surface = VulkanTestEnvironment.InvokeOnRenderThread(() =>
            SKSurface.Create(
                graphics.SkiaContext,
                false,
                new SKImageInfo(8, 8, SKColorType.RgbaF16, SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear()))
            ?? throw new InvalidOperationException("Could not create a GPU surface."));
        // Built on a thread pool thread, as a caller-supplied surface can be, so no dispatcher owns the target and
        // nothing says which thread its context belongs to.
        RenderTarget target = await Task.Run(() => (RenderTarget)new DispatcherlessRenderTarget(surface, 8, 8));

        (bool completedOnReturn, Task<Bitmap> pending) = VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            target.Value.Canvas.Clear(SKColors.White);
            Task<Bitmap> pending = target.SnapshotAsync();
            return (pending.IsCompleted, pending);
        });

        try
        {
            using Bitmap snapshot = await pending;
            Assert.Multiple(() =>
            {
                Assert.That(completedOnReturn, Is.True, "Only the calling thread is known to own the context, so the read must not wait for a poll elsewhere.");
                Assert.That(ReadRed(snapshot, 4, 4), Is.EqualTo(1f).Within(1e-3f));
            });
        }
        finally
        {
            VulkanTestEnvironment.InvokeOnRenderThread(target.Dispose);
        }
    }

    [Test]
    public async Task RendererSnapshotAsync_ReadsTheSamePixelsAsSnapshot()
    {
        VulkanTestEnvironment.EnsureAvailable();
        (Renderer renderer, Bitmap expected, Task<Bitmap> pending) = VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var renderer = new Renderer(64, 48, RenderIntent.Preview);
            var shape = new RectShape
            {
                Width = { CurrentValue = 32 },
                Height = { CurrentValue = 24 },
                Fill = { CurrentValue = Brushes.White },
            };
            var resource = (Drawable.Resource)shape.ToResource(CompositionContext.Default);
            renderer.Render(new CompositionFrame(
                ImmutableArray.Create<EngineObject.Resource>(resource),
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(64, 48),
                null));

            return (renderer, renderer.Snapshot(), renderer.SnapshotAsync());
        });

        try
        {
            using Bitmap actual = await pending;
            Assert.Multiple(() =>
            {
                // Without something drawn, any blank bitmap would match the blank snapshot.
                Assert.That(expected.GetPixelSpan().ToArray(), Has.Some.Not.Zero);
                Assert.That(actual.GetPixelSpan().SequenceEqual(expected.GetPixelSpan()), Is.True);
            });
        }
        finally
        {
            expected.Dispose();
            VulkanTestEnvironment.InvokeOnRenderThread(renderer.Dispose);
        }
    }

    [Test]
    public void SurfaceReadback_CompletedWithoutAResult_FailsAndDisposesItsBitmap()
    {
        Bitmap destination = CreateReadbackBitmap();
        var readback = new RenderTarget.SurfaceReadback(destination);

        readback.Complete(null);

        Assert.Multiple(() =>
        {
            Assert.That(readback.Completion.Exception?.InnerException, Is.InstanceOf<InvalidOperationException>());
            Assert.That(destination.IsDisposed, Is.True);
        });
    }

    [Test]
    public void SurfaceReadback_FailsWhenTheContextIsAbandonedWhilePolling()
    {
        Dispatcher dispatcher = Dispatcher.Spawn();
        try
        {
            Bitmap destination = CreateReadbackBitmap();
            var readback = new RenderTarget.SurfaceReadback(destination);

            readback.PollUntilComplete(dispatcher, () => { }, () => true);

            Assert.That(
                async () => await readback.Completion.WaitAsync(TimeSpan.FromSeconds(5)),
                Throws.InstanceOf<ObjectDisposedException>());
            Assert.That(destination.IsDisposed, Is.True);
        }
        finally
        {
            dispatcher.Shutdown();
        }
    }

    [Test]
    public void SurfaceReadback_FailsWithWhatPollingThrows()
    {
        Dispatcher dispatcher = Dispatcher.Spawn();
        try
        {
            Bitmap destination = CreateReadbackBitmap();
            var readback = new RenderTarget.SurfaceReadback(destination);

            readback.PollUntilComplete(
                dispatcher,
                () => throw new InvalidOperationException("The context could not be polled."),
                () => false);

            Assert.That(
                async () => await readback.Completion.WaitAsync(TimeSpan.FromSeconds(5)),
                Throws.InstanceOf<InvalidOperationException>().With.Message.EqualTo("The context could not be polled."));
            Assert.That(destination.IsDisposed, Is.True);
        }
        finally
        {
            dispatcher.Shutdown();
        }
    }

    [Test]
    public async Task SurfaceReadback_OnADispatcherThatHasAlreadyShutDown_FailsAtOnce()
    {
        Dispatcher dispatcher = Dispatcher.Spawn();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.ShutdownFinished += (_, _) => finished.TrySetResult();
        dispatcher.Shutdown();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Bitmap destination = CreateReadbackBitmap();
        var readback = new RenderTarget.SurfaceReadback(destination);

        readback.PollUntilComplete(dispatcher, () => { }, () => false);

        Assert.Multiple(() =>
        {
            Assert.That(readback.Completion.Exception?.InnerException, Is.InstanceOf<OperationCanceledException>());
            Assert.That(destination.IsDisposed, Is.True);
        });
    }

    [Test]
    public void SurfaceReadback_KeepsPollingWhileTheDispatcherIsNeverIdle()
    {
        Dispatcher dispatcher = Dispatcher.Spawn();
        var stop = new CancellationTokenSource();
        try
        {
            // Medium work that queues itself again before it returns, the way renders can stay queued during playback,
            // so the dispatcher's queue never empties.
            void Busy()
            {
                if (!stop.IsCancellationRequested)
                    dispatcher.Dispatch(Busy, DispatchPriority.Medium);
            }

            dispatcher.Dispatch(Busy, DispatchPriority.Medium);
            var readback = new RenderTarget.SurfaceReadback(CreateReadbackBitmap());
            var polled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            readback.PollUntilComplete(dispatcher, () => polled.TrySetResult(), () => false);

            Assert.That(
                polled.Task.Wait(TimeSpan.FromSeconds(5)),
                Is.True,
                "A poll has to get its turn behind work that never lets the queue empty.");
        }
        finally
        {
            stop.Cancel();
            dispatcher.Shutdown();
        }
    }

    private static Bitmap CreateReadbackBitmap()
        => new(4, 4, BitmapColorType.RgbaF16, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);

    private sealed class DispatcherlessRenderTarget(SKSurface surface, int width, int height)
        : RenderTarget(surface, width, height);

    private static RenderTarget CreateGpuTarget(int width, int height)
    {
        return RenderTarget.Create(width, height)
               ?? throw new InvalidOperationException("Could not create a GPU render target.");
    }

    private static float ReadRed(Bitmap bitmap, int x, int y)
    {
        ReadOnlySpan<ushort> channels = bitmap.GetPixelSpan<ushort>();
        return (float)BitConverter.UInt16BitsToHalf(channels[((y * bitmap.Width) + x) * 4]);
    }

    private sealed class DisposalProbe : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
