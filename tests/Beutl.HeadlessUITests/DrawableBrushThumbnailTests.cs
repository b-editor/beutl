using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Subjects;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Threading;
using AvaDispatcher = Avalonia.Threading.Dispatcher;
using AvaImageBrush = Avalonia.Media.ImageBrush;
using AvaPixelSize = Avalonia.PixelSize;
using AvaPropertyChangedEventArgs = Avalonia.AvaloniaPropertyChangedEventArgs;
using AvaStretch = Avalonia.Media.Stretch;

namespace Beutl.HeadlessUITests;

[NonParallelizable]
[TestFixture]
public class DrawableBrushThumbnailTests
{
    [AvaloniaTest]
    public async Task Update_publishes_initial_thumbnail_and_propagates_resource_changes()
    {
        GpuTestGate.EnsureAvailable();
        var drawableBrush = new DrawableBrush(CreateRectangle(40, 24, Colors.Red));
        drawableBrush.Stretch.CurrentValue = Stretch.Uniform;
        var resource = (DrawableBrush.Resource)drawableBrush.ToResource(new CompositionContext(TimeSpan.Zero));
        var imageBrush = new AvaImageBrush();
        var handler = new AvaloniaTypeConverter.DrawableImageBrushHandler(resource, imageBrush);

        try
        {
            handler.Update();
            await WaitUntilAsync(
                () => imageBrush.Source is WriteableBitmap bitmap
                      && bitmap.PixelSize == new AvaPixelSize(40, 24),
                TimeSpan.FromSeconds(5));

            var first = (WriteableBitmap)imageBrush.Source!;
            uint initialPixel = SamplePixel(first, 20, 12);
            Assert.That(imageBrush.Stretch, Is.EqualTo(AvaStretch.Uniform));

            drawableBrush.Drawable.CurrentValue = CreateRectangle(72, 36, Colors.Blue);
            drawableBrush.Stretch.CurrentValue = Stretch.None;
            UpdateResource(resource, drawableBrush);
            handler.Update();

            await WaitUntilAsync(
                () => imageBrush.Source is WriteableBitmap bitmap
                      && !ReferenceEquals(bitmap, first)
                      && bitmap.PixelSize == new AvaPixelSize(72, 36),
                TimeSpan.FromSeconds(5));
            var second = (WriteableBitmap)imageBrush.Source!;
            uint updatedPixel = SamplePixel(second, 36, 18);

            Assert.Multiple(() =>
            {
                Assert.That(imageBrush.Stretch, Is.EqualTo(AvaStretch.None));
                Assert.That(imageBrush.Source, Is.Not.SameAs(first));
                Assert.That(
                    second.PixelSize,
                    Is.EqualTo(new AvaPixelSize(72, 36)));
                Assert.That((initialPixel & 0xFF), Is.GreaterThan(200), "The initial thumbnail must be red.");
                Assert.That(((initialPixel >> 16) & 0xFF), Is.LessThan(30), "The initial thumbnail must not be blue.");
                Assert.That(((updatedPixel >> 16) & 0xFF), Is.GreaterThan(200), "The updated thumbnail must be blue.");
                Assert.That((updatedPixel & 0xFF), Is.LessThan(30), "The updated thumbnail must not be red.");
                Assert.That((initialPixel >> 24), Is.GreaterThan(200));
                Assert.That((updatedPixel >> 24), Is.GreaterThan(200));
            });
        }
        finally
        {
            handler.Dispose();
            await WaitUntilAsync(() => resource.IsDisposed, TimeSpan.FromSeconds(5));
        }

        Assert.That(imageBrush.Source, Is.Null);
    }

    private static uint SamplePixel(WriteableBitmap bitmap, int x, int y)
    {
        using ILockedFramebuffer buffer = bitmap.Lock();
        Assert.That(
            buffer.Format,
            Is.EqualTo(Avalonia.Platform.PixelFormat.Rgba8888)
                .Or.EqualTo(Avalonia.Platform.PixelFormat.Bgra8888),
            $"SamplePixel assumes a 32bpp RGBA or BGRA bitmap but the format is {buffer.Format}.");
        Assert.That(x, Is.InRange(0, buffer.Size.Width - 1));
        Assert.That(y, Is.InRange(0, buffer.Size.Height - 1));

        uint pixel;
        unsafe
        {
            byte* row = (byte*)buffer.Address + (y * buffer.RowBytes);
            pixel = ((uint*)row)[x];
        }

        return buffer.Format == Avalonia.Platform.PixelFormat.Bgra8888
            ? (pixel & 0xFF00FF00) | ((pixel & 0x000000FF) << 16) | ((pixel & 0x00FF0000) >> 16)
            : pixel;
    }

    [AvaloniaTest]
    public async Task Superseded_update_never_publishes_its_stale_thumbnail()
    {
        GpuTestGate.EnsureAvailable();
        var staleDrawable = new BlockingThumbnailDrawable(40, 24, Brushes.Resource.Red);
        var drawableBrush = new DrawableBrush(staleDrawable);
        var resource = (DrawableBrush.Resource)drawableBrush.ToResource(new CompositionContext(TimeSpan.Zero));
        var imageBrush = new AvaImageBrush();
        var publishedSizes = new ConcurrentQueue<AvaPixelSize>();
        imageBrush.PropertyChanged += (_, args) =>
        {
            if (args.Property == AvaImageBrush.SourceProperty
                && imageBrush.Source is WriteableBitmap bitmap)
            {
                publishedSizes.Enqueue(bitmap.PixelSize);
            }
        };
        var handler = new AvaloniaTypeConverter.DrawableImageBrushHandler(resource, imageBrush);

        try
        {
            handler.Update();
            await staleDrawable.RenderEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            drawableBrush.Drawable.CurrentValue = CreateRectangle(72, 36, Colors.Blue);
            UpdateResource(resource, drawableBrush);
            handler.Update();
            staleDrawable.ReleaseRender();

            await WaitUntilAsync(
                () => imageBrush.Source is WriteableBitmap bitmap
                      && bitmap.PixelSize == new AvaPixelSize(72, 36),
                TimeSpan.FromSeconds(5));

            Assert.That(publishedSizes.ToArray(), Is.EqualTo(new[] { new AvaPixelSize(72, 36) }));
        }
        finally
        {
            staleDrawable.ReleaseRender();
            handler.Dispose();
            await WaitUntilAsync(() => resource.IsDisposed, TimeSpan.FromSeconds(5));
        }
    }

    [AvaloniaTest]
    public async Task Superseded_queued_publication_is_discarded_before_ui_delivery()
    {
        GpuTestGate.EnsureAvailable();
        var drawableBrush = new DrawableBrush(CreateRectangle(40, 24, Colors.Red));
        var resource = (DrawableBrush.Resource)drawableBrush.ToResource(new CompositionContext(TimeSpan.Zero));
        var imageBrush = new AvaImageBrush();
        var publishedSizes = new ConcurrentQueue<AvaPixelSize>();
        imageBrush.PropertyChanged += (_, args) =>
        {
            if (args.Property == AvaImageBrush.SourceProperty
                && imageBrush.Source is WriteableBitmap bitmap)
            {
                publishedSizes.Enqueue(bitmap.PixelSize);
            }
        };
        var handler = new AvaloniaTypeConverter.DrawableImageBrushHandler(resource, imageBrush);

        try
        {
            Assert.That(AvaDispatcher.UIThread.CheckAccess(), Is.True);
            handler.Update();
            RenderThread.Dispatcher.Invoke(
                static () => { },
                DispatchPriority.Low,
                CancellationToken.None);

            drawableBrush.Drawable.CurrentValue = CreateRectangle(72, 36, Colors.Blue);
            UpdateResource(resource, drawableBrush);
            handler.Update();

            await WaitUntilAsync(
                () => imageBrush.Source is WriteableBitmap bitmap
                      && bitmap.PixelSize == new AvaPixelSize(72, 36),
                TimeSpan.FromSeconds(5));

            Assert.That(
                publishedSizes.ToArray(),
                Is.EqualTo(new[] { new AvaPixelSize(72, 36) }));
        }
        finally
        {
            handler.Dispose();
            await WaitUntilAsync(() => resource.IsDisposed, TimeSpan.FromSeconds(5));
        }
    }

    [AvaloniaTest]
    public async Task Reentrant_source_callback_can_dispose_without_lock_inversion()
    {
        GpuTestGate.EnsureAvailable();
        var drawableBrush = new DrawableBrush(CreateRectangle(40, 24, Colors.Red));
        var resource = (DrawableBrush.Resource)drawableBrush.ToResource(new CompositionContext(TimeSpan.Zero));
        var imageBrush = new AvaImageBrush();
        var handler = new AvaloniaTypeConverter.DrawableImageBrushHandler(resource, imageBrush);
        var callbackResult = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Task? disposalTask = null;
        imageBrush.PropertyChanged += (_, args) =>
        {
            if (args.Property != AvaImageBrush.SourceProperty
                || imageBrush.Source is not WriteableBitmap)
            {
                return;
            }

            disposalTask = Task.Run(() =>
            {
                handler.Dispose();
                handler.Dispose();
            });
            callbackResult.TrySetResult(disposalTask.Wait(TimeSpan.FromSeconds(1)));
        };

        try
        {
            handler.Update();

            Assert.That(
                await callbackResult.Task.WaitAsync(TimeSpan.FromSeconds(5)),
                Is.True,
                "A synchronous Source callback must not wait on the handler publication lock.");
            await disposalTask!.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(
                () => resource.IsDisposed && imageBrush.Source is null,
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            handler.Dispose();
            await WaitUntilAsync(() => resource.IsDisposed, TimeSpan.FromSeconds(5));
        }

        Assert.That(imageBrush.Source, Is.Null);
    }

    [AvaloniaTest]
    public async Task Superseding_update_during_stretch_notification_cancels_pending_source_publication()
    {
        GpuTestGate.EnsureAvailable();
        var drawableBrush = new DrawableBrush(CreateRectangle(40, 24, Colors.Red));
        drawableBrush.Stretch.CurrentValue = Stretch.Uniform;
        var resource = (DrawableBrush.Resource)drawableBrush.ToResource(new CompositionContext(TimeSpan.Zero));
        var imageBrush = new AvaImageBrush();
        var handler = new AvaloniaTypeConverter.DrawableImageBrushHandler(resource, imageBrush);

        try
        {
            handler.Update();
            await WaitUntilAsync(
                () => imageBrush.Source is WriteableBitmap bitmap
                      && bitmap.PixelSize == new AvaPixelSize(40, 24),
                TimeSpan.FromSeconds(5));

            var first = (WriteableBitmap)imageBrush.Source!;
            var replacementPublications = new ConcurrentQueue<WriteableBitmap>();
            imageBrush.PropertyChanged += (_, args) =>
            {
                if (args.Property == AvaImageBrush.SourceProperty
                    && imageBrush.Source is WriteableBitmap bitmap
                    && !ReferenceEquals(bitmap, first))
                {
                    replacementPublications.Enqueue(bitmap);
                }
            };

            int superseded = 0;
            bool supersedingUpdateCompleted = false;
            imageBrush.PropertyChanged += (_, args) =>
            {
                if (args.Property == AvaImageBrush.StretchProperty
                    && imageBrush.Stretch == AvaStretch.None
                    && Interlocked.Exchange(ref superseded, 1) == 0)
                {
                    supersedingUpdateCompleted = Task.Run(handler.Update)
                        .Wait(TimeSpan.FromSeconds(1));
                }
            };

            drawableBrush.Drawable.CurrentValue = CreateRectangle(72, 36, Colors.Blue);
            drawableBrush.Stretch.CurrentValue = Stretch.None;
            UpdateResource(resource, drawableBrush);
            handler.Update();

            await WaitUntilAsync(
                () => imageBrush.Source is WriteableBitmap bitmap
                      && !ReferenceEquals(bitmap, first)
                      && bitmap.PixelSize == new AvaPixelSize(72, 36),
                TimeSpan.FromSeconds(5));
            RenderThread.Dispatcher.Invoke(
                static () => { },
                DispatchPriority.Low,
                CancellationToken.None);
            AvaDispatcher.UIThread.RunJobs();

            Assert.Multiple(() =>
            {
                Assert.That(
                    supersedingUpdateCompleted,
                    Is.True,
                    "A superseding update must be able to cancel the publishing update outside the gate.");
                Assert.That(
                    replacementPublications.Count,
                    Is.EqualTo(1),
                    "The canceled publication must not assign its stale bitmap before the replacement.");
            });
        }
        finally
        {
            handler.Dispose();
            await WaitUntilAsync(() => resource.IsDisposed, TimeSpan.FromSeconds(5));
        }

        Assert.That(imageBrush.Source, Is.Null);
    }

    [AvaloniaTest]
    public async Task Reentrant_source_callback_that_restores_previous_bitmap_prevents_commit()
    {
        GpuTestGate.EnsureAvailable();
        var drawableBrush = new DrawableBrush(CreateRectangle(40, 24, Colors.Red));
        drawableBrush.Stretch.CurrentValue = Stretch.Uniform;
        var resource = (DrawableBrush.Resource)drawableBrush.ToResource(new CompositionContext(TimeSpan.Zero));
        var imageBrush = new AvaImageBrush();
        var handler = new AvaloniaTypeConverter.DrawableImageBrushHandler(resource, imageBrush);

        try
        {
            handler.Update();
            await WaitUntilAsync(
                () => imageBrush.Source is WriteableBitmap bitmap
                      && bitmap.PixelSize == new AvaPixelSize(40, 24),
                TimeSpan.FromSeconds(5));

            var first = (WriteableBitmap)imageBrush.Source!;
            WriteableBitmap? rejected = null;
            EventHandler<AvaPropertyChangedEventArgs> restorePrevious = (_, args) =>
            {
                if (args.Property == AvaImageBrush.SourceProperty
                    && imageBrush.Source is WriteableBitmap bitmap
                    && !ReferenceEquals(bitmap, first)
                    && rejected is null)
                {
                    rejected = bitmap;
                    imageBrush.Source = first;
                }
            };
            imageBrush.PropertyChanged += restorePrevious;

            drawableBrush.Drawable.CurrentValue = CreateRectangle(72, 36, Colors.Blue);
            drawableBrush.Stretch.CurrentValue = Stretch.None;
            UpdateResource(resource, drawableBrush);
            handler.Update();

            await WaitUntilAsync(
                () => rejected is not null && ReferenceEquals(imageBrush.Source, first),
                TimeSpan.FromSeconds(5));
            imageBrush.PropertyChanged -= restorePrevious;

            Assert.Multiple(() =>
            {
                Assert.That(imageBrush.Source, Is.SameAs(first));
                Assert.That(imageBrush.Stretch, Is.EqualTo(AvaStretch.Uniform));
            });
            using (first.Lock())
            {
            }

            Assert.That(
                CanLock(rejected!),
                Is.False,
                "The rejected bitmap must be disposed after the reentrant publication is rolled back.");
        }
        finally
        {
            handler.Dispose();
            await WaitUntilAsync(() => resource.IsDisposed, TimeSpan.FromSeconds(5));
        }

        Assert.That(imageBrush.Source, Is.Null);
    }

    [AvaloniaTest]
    public async Task Throwing_publication_callbacks_roll_back_bitmap_and_stretch_ownership()
    {
        GpuTestGate.EnsureAvailable();
        var drawableBrush = new DrawableBrush(CreateRectangle(40, 24, Colors.Red));
        drawableBrush.Stretch.CurrentValue = Stretch.Uniform;
        var resource = (DrawableBrush.Resource)drawableBrush.ToResource(new CompositionContext(TimeSpan.Zero));
        var imageBrush = new AvaImageBrush();
        var handler = new AvaloniaTypeConverter.DrawableImageBrushHandler(resource, imageBrush);

        try
        {
            handler.Update();
            await WaitUntilAsync(
                () => imageBrush.Source is WriteableBitmap bitmap
                      && bitmap.PixelSize == new AvaPixelSize(40, 24),
                TimeSpan.FromSeconds(5));

            var first = (WriteableBitmap)imageBrush.Source!;
            WriteableBitmap? rejected = null;
            EventHandler<AvaPropertyChangedEventArgs> throwOnReplacement = (_, args) =>
            {
                if (args.Property == AvaImageBrush.SourceProperty
                    && imageBrush.Source is WriteableBitmap bitmap
                    && !ReferenceEquals(bitmap, first))
                {
                    rejected = bitmap;
                    throw new InvalidOperationException("Injected publication failure.");
                }
            };
            imageBrush.PropertyChanged += throwOnReplacement;
            int stretchRollbackFailures = 0;
            EventHandler<AvaPropertyChangedEventArgs> throwOnStretchRollback = (_, args) =>
            {
                if (args.Property == AvaImageBrush.StretchProperty
                    && imageBrush.Stretch == AvaStretch.Uniform
                    && rejected is not null)
                {
                    stretchRollbackFailures++;
                    throw new InvalidOperationException("Injected stretch rollback failure.");
                }
            };
            imageBrush.PropertyChanged += throwOnStretchRollback;

            drawableBrush.Drawable.CurrentValue = CreateRectangle(72, 36, Colors.Blue);
            drawableBrush.Stretch.CurrentValue = Stretch.None;
            UpdateResource(resource, drawableBrush);
            handler.Update();

            await WaitUntilAsync(
                () => rejected is not null && ReferenceEquals(imageBrush.Source, first),
                TimeSpan.FromSeconds(5));
            imageBrush.PropertyChanged -= throwOnReplacement;
            imageBrush.PropertyChanged -= throwOnStretchRollback;

            Assert.Multiple(() =>
            {
                Assert.That(imageBrush.Source, Is.SameAs(first));
                Assert.That(imageBrush.Stretch, Is.EqualTo(AvaStretch.Uniform));
                Assert.That(rejected, Is.Not.Null);
                Assert.That(rejected, Is.Not.SameAs(first));
                Assert.That(stretchRollbackFailures, Is.EqualTo(1));
            });
            using (first.Lock())
            {
            }

            Assert.That(
                () =>
                {
                    using var _ = rejected!.Lock();
                },
                Throws.Exception);

            drawableBrush.Drawable.CurrentValue = CreateRectangle(96, 48, Colors.Green);
            drawableBrush.Stretch.CurrentValue = Stretch.UniformToFill;
            UpdateResource(resource, drawableBrush);
            handler.Update();

            await WaitUntilAsync(
                () => imageBrush.Source is WriteableBitmap bitmap
                      && bitmap.PixelSize == new AvaPixelSize(96, 48),
                TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(imageBrush.Source, Is.Not.SameAs(first));
                Assert.That(imageBrush.Source, Is.Not.SameAs(rejected));
                Assert.That(imageBrush.Stretch, Is.EqualTo(AvaStretch.UniformToFill));
            });
        }
        finally
        {
            handler.Dispose();
            await WaitUntilAsync(() => resource.IsDisposed, TimeSpan.FromSeconds(5));
        }

        Assert.That(imageBrush.Source, Is.Null);
    }

    [AvaloniaTest]
    public async Task Dispose_during_empty_update_cancels_publication_and_releases_resource_once_idle()
    {
        var blockingDrawable = new BlockingThumbnailDrawable(0, 0, null);
        var drawableBrush = new DrawableBrush(blockingDrawable);
        var resource = (DrawableBrush.Resource)drawableBrush.ToResource(new CompositionContext(TimeSpan.Zero));
        var imageBrush = new AvaImageBrush();
        var handler = new AvaloniaTypeConverter.DrawableImageBrushHandler(resource, imageBrush);

        try
        {
            handler.Update();
            await blockingDrawable.RenderEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            handler.Dispose();
            Assert.Multiple(() =>
            {
                Assert.That(resource.IsDisposed, Is.False,
                    "The resource must remain alive until the render-thread update exits.");
                Assert.That(imageBrush.Source, Is.Null);
            });

            blockingDrawable.ReleaseRender();
            await WaitUntilAsync(() => resource.IsDisposed, TimeSpan.FromSeconds(5));

            handler.Update();
            await RenderThread.Dispatcher.InvokeAsync(
                static () => { },
                DispatchPriority.Low,
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(resource.IsDisposed, Is.True);
                Assert.That(imageBrush.Source, Is.Null);
            });

            handler.Dispose();
        }
        finally
        {
            blockingDrawable.ReleaseRender();
            handler.Dispose();
        }
    }

    [AvaloniaTest]
    public void Immediate_subscription_disposal_cancels_resource_creation()
    {
        var blockerEntered = new ManualResetEventSlim();
        var releaseBlocker = new ManualResetEventSlim();
        IDisposable? subscription = null;

        try
        {
            RenderThread.Dispatcher.Dispatch(
                () =>
                {
                    blockerEntered.Set();
                    releaseBlocker.Wait();
                },
                DispatchPriority.High);
            Assert.That(blockerEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            var drawableBrush = new DisposalTrackingDrawableBrush();
            using var clock = new BehaviorSubject<TimeSpan>(TimeSpan.Zero);
            (_, subscription, _) = drawableBrush.ToAvaBrushSync(clock);
            subscription.Dispose();

            releaseBlocker.Set();
            RenderThread.Dispatcher.Invoke(
                static () => { },
                DispatchPriority.Low,
                CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(drawableBrush.ResourceUpdateCalls, Is.Zero);
                Assert.That(drawableBrush.ResourceDisposeCalls, Is.Zero);
            });
        }
        finally
        {
            subscription?.Dispose();
            releaseBlocker.Set();
            RenderThread.Dispatcher.Invoke(
                static () => { },
                DispatchPriority.Low,
                CancellationToken.None);
            blockerEntered.Dispose();
            releaseBlocker.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task Resource_lifetime_and_updates_are_serialized_on_render_dispatcher()
    {
        var drawableBrush = new DisposalTrackingDrawableBrush();
        using var clock = new BehaviorSubject<TimeSpan>(TimeSpan.Zero);
        (_, IDisposable subscription, _) = drawableBrush.ToAvaBrushSync(clock);

        await WaitUntilAsync(
            () => drawableBrush.ResourceUpdateCalls == 1,
            TimeSpan.FromSeconds(5));

        clock.OnNext(TimeSpan.FromSeconds(1));
        await WaitUntilAsync(
            () => drawableBrush.ResourceUpdateCalls == 2,
            TimeSpan.FromSeconds(5));

        subscription.Dispose();

        await WaitUntilAsync(
            () => drawableBrush.ResourceDisposeCalls == 1,
            TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(drawableBrush.ResourceDisposeCalls, Is.EqualTo(1));
            Assert.That(
                drawableBrush.ResourceCreationThreadId,
                Is.EqualTo(RenderThread.Dispatcher.Thread.ManagedThreadId));
            Assert.That(
                drawableBrush.LastResourceUpdateThreadId,
                Is.EqualTo(RenderThread.Dispatcher.Thread.ManagedThreadId));
            Assert.That(
                drawableBrush.ResourceDisposalThreadId,
                Is.EqualTo(RenderThread.Dispatcher.Thread.ManagedThreadId));
        });
    }

    [AvaloniaTest]
    public void Render_dispatcher_shutdown_abandons_queued_update_and_releases_resource()
    {
        var drawableBrush = new DrawableBrush();
        var resource = (DrawableBrush.Resource)drawableBrush.ToResource(
            new CompositionContext(TimeSpan.Zero));
        var imageBrush = new AvaImageBrush();
        var blockerEntered = new ManualResetEventSlim();
        var releaseBlocker = new ManualResetEventSlim();
        Dispatcher dispatcher = Dispatcher.Spawn();
        var handler = new AvaloniaTypeConverter.DrawableImageBrushHandler(
            resource,
            imageBrush,
            dispatcher);

        try
        {
            dispatcher.Dispatch(
                () =>
                {
                    blockerEntered.Set();
                    releaseBlocker.Wait();
                },
                DispatchPriority.High);
            Assert.That(blockerEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            handler.Update();
            handler.Dispose();
            Assert.That(resource.IsDisposed, Is.False);

            dispatcher.Shutdown();

            Assert.That(
                resource.IsDisposed, Is.False,
                "the dispatcher thread is still inside the blocked operation");

            releaseBlocker.Set();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(5)), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(resource.IsDisposed, Is.True);
                Assert.That(imageBrush.Source, Is.Null);
            });
        }
        finally
        {
            handler.Dispose();
            if (!dispatcher.HasShutdownStarted)
                dispatcher.Shutdown();
            releaseBlocker.Set();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(5)), Is.True);
            blockerEntered.Dispose();
            releaseBlocker.Dispose();
            resource.Dispose();
        }
    }

    // A dispose with no update pending settles at once and hands the release to the render dispatcher.
    // Shutdown abandons whatever is still queued, so the handler has to stay able to recover that release
    // instead of writing it off the moment it is queued - but not before the dispatcher thread has stopped,
    // since the blocked operation below stands in for a frame still reading the resource.
    [AvaloniaTest]
    public void Render_dispatcher_shutdown_recovers_a_release_it_abandoned()
    {
        var drawableBrush = new DisposalTrackingDrawableBrush();
        var resource = (DrawableBrush.Resource)drawableBrush.ToResource(
            new CompositionContext(TimeSpan.Zero));
        var imageBrush = new AvaImageBrush();
        var blockerEntered = new ManualResetEventSlim();
        var releaseBlocker = new ManualResetEventSlim();
        Dispatcher dispatcher = Dispatcher.Spawn();
        var handler = new AvaloniaTypeConverter.DrawableImageBrushHandler(
            resource,
            imageBrush,
            dispatcher);

        try
        {
            dispatcher.Dispatch(
                () =>
                {
                    blockerEntered.Set();
                    releaseBlocker.Wait();
                },
                DispatchPriority.High);
            Assert.That(blockerEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            handler.Dispose();
            Assert.That(resource.IsDisposed, Is.False, "the release is queued behind the blocked operation");

            dispatcher.Shutdown();

            Assert.That(
                resource.IsDisposed, Is.False,
                "the dispatcher thread is still inside the blocked operation");

            releaseBlocker.Set();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(5)), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(resource.IsDisposed, Is.True);
                Assert.That(
                    drawableBrush.ResourceDisposeCalls,
                    Is.EqualTo(1),
                    "draining the queue must not release the resource a second time");
            });
        }
        finally
        {
            handler.Dispose();
            if (!dispatcher.HasShutdownStarted)
                dispatcher.Shutdown();
            releaseBlocker.Set();
            Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(5)), Is.True);
            blockerEntered.Dispose();
            releaseBlocker.Dispose();
            resource.Dispose();
        }
    }

    // An ImageBrush.Source subscriber is arbitrary code and may throw out of the assignment that clears the
    // source. Dispose has already latched _disposeRequested by then, so a throw escaping the clear turns every
    // later Dispose into a no-op and strands the published bitmap and the owned resource for good.
    [AvaloniaTest]
    public async Task Dispose_completes_its_cleanup_when_a_source_subscriber_throws()
    {
        GpuTestGate.EnsureAvailable();
        var drawableBrush = new DrawableBrush(CreateRectangle(40, 24, Colors.Red));
        drawableBrush.Stretch.CurrentValue = Stretch.Uniform;
        var resource = (DrawableBrush.Resource)drawableBrush.ToResource(new CompositionContext(TimeSpan.Zero));
        var imageBrush = new AvaImageBrush();
        var handler = new AvaloniaTypeConverter.DrawableImageBrushHandler(resource, imageBrush);
        var failure = new InvalidOperationException("An ImageBrush.Source subscriber refused the change.");

        void Reject(object? sender, AvaPropertyChangedEventArgs e)
        {
            if (e.Property == AvaImageBrush.SourceProperty)
                throw failure;
        }

        try
        {
            handler.Update();
            await WaitUntilAsync(
                () => imageBrush.Source is WriteableBitmap bitmap
                      && bitmap.PixelSize == new AvaPixelSize(40, 24),
                TimeSpan.FromSeconds(5));
            var published = (WriteableBitmap)imageBrush.Source!;

            imageBrush.PropertyChanged += Reject;
            try
            {
                Assert.That(
                    handler.Dispose,
                    Throws.Exception.SameAs(failure),
                    "The subscriber's failure still belongs to the caller.");
            }
            finally
            {
                imageBrush.PropertyChanged -= Reject;
            }

            await WaitUntilAsync(() => resource.IsDisposed, TimeSpan.FromSeconds(5));
            Assert.That(
                CanLock(published), Is.False,
                "The published bitmap has no owner left once the handler has let go of it.");
        }
        finally
        {
            imageBrush.PropertyChanged -= Reject;
            handler.Dispose();
            resource.Dispose();
        }
    }

    [AvaloniaTest]
    public void Handler_attached_to_an_already_stopped_dispatcher_still_releases_its_resource()
    {
        var drawableBrush = new DrawableBrush();
        var resource = (DrawableBrush.Resource)drawableBrush.ToResource(
            new CompositionContext(TimeSpan.Zero));
        var imageBrush = new AvaImageBrush();
        Dispatcher dispatcher = Dispatcher.Spawn();
        var loopEntered = new ManualResetEventSlim();

        // Shutdown racing Start is swallowed and leaves the loop running forever, so wait until the
        // loop is demonstrably live before stopping it.
        dispatcher.Dispatch(loopEntered.Set, DispatchPriority.High);
        Assert.That(loopEntered.Wait(TimeSpan.FromSeconds(5)), Is.True);
        dispatcher.Shutdown();
        Assert.That(dispatcher.Thread.Join(TimeSpan.FromSeconds(5)), Is.True);

        // ShutdownStarted is one-shot and already fired, so this handler never receives it.
        var handler = new AvaloniaTypeConverter.DrawableImageBrushHandler(
            resource,
            imageBrush,
            dispatcher);

        try
        {
            handler.Update();
            handler.Dispose();

            Assert.That(resource.IsDisposed, Is.True);
        }
        finally
        {
            handler.Dispose();
            loopEntered.Dispose();
            resource.Dispose();
        }
    }

    [AvaloniaTest]
    public async Task Grouped_content_publishes_a_thumbnail()
    {
        GpuTestGate.EnsureAvailable();
        var group = new DrawableGroup();
        group.Children.Add(CreateRectangle(40, 24, Colors.Red));
        var drawableBrush = new DrawableBrush(group);
        drawableBrush.Stretch.CurrentValue = Stretch.Uniform;
        var resource = (DrawableBrush.Resource)drawableBrush.ToResource(new CompositionContext(TimeSpan.Zero));
        var imageBrush = new AvaImageBrush();
        var handler = new AvaloniaTypeConverter.DrawableImageBrushHandler(resource, imageBrush);

        try
        {
            handler.Update();
            await WaitUntilAsync(
                () => imageBrush.Source is WriteableBitmap,
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            handler.Dispose();
            await WaitUntilAsync(() => resource.IsDisposed, TimeSpan.FromSeconds(5));
        }
    }

    // Dispose clears the published thumbnail, so a rollback triggered by that same disposal must not
    // put the previous one back: the handler would be gone while still owning the bitmap it reinstated.
    [AvaloniaTest]
    public async Task Disposal_during_stretch_notification_leaves_no_thumbnail_behind()
    {
        GpuTestGate.EnsureAvailable();
        var drawableBrush = new DrawableBrush(CreateRectangle(40, 24, Colors.Red));
        drawableBrush.Stretch.CurrentValue = Stretch.Uniform;
        var resource = (DrawableBrush.Resource)drawableBrush.ToResource(new CompositionContext(TimeSpan.Zero));
        var imageBrush = new AvaImageBrush();
        var handler = new AvaloniaTypeConverter.DrawableImageBrushHandler(resource, imageBrush);

        try
        {
            handler.Update();
            await WaitUntilAsync(
                () => imageBrush.Source is WriteableBitmap bitmap
                      && bitmap.PixelSize == new AvaPixelSize(40, 24),
                TimeSpan.FromSeconds(5));

            int disposedOnce = 0;
            imageBrush.PropertyChanged += (_, args) =>
            {
                if (args.Property == AvaImageBrush.StretchProperty
                    && imageBrush.Stretch == AvaStretch.None
                    && Interlocked.Exchange(ref disposedOnce, 1) == 0)
                {
                    handler.Dispose();
                }
            };

            drawableBrush.Drawable.CurrentValue = CreateRectangle(72, 36, Colors.Blue);
            drawableBrush.Stretch.CurrentValue = Stretch.None;
            UpdateResource(resource, drawableBrush);
            handler.Update();

            await WaitUntilAsync(() => disposedOnce == 1, TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => resource.IsDisposed, TimeSpan.FromSeconds(5));

            Assert.That(imageBrush.Source, Is.Null,
                "a disposed handler must not leave the previous thumbnail installed");
        }
        finally
        {
            handler.Dispose();
            await WaitUntilAsync(() => resource.IsDisposed, TimeSpan.FromSeconds(5));
        }
    }

    private static RectShape CreateRectangle(float width, float height, Color color)
    {
        return new RectShape
        {
            Width = { CurrentValue = width },
            Height = { CurrentValue = height },
            Fill = { CurrentValue = new SolidColorBrush(color) },
        };
    }

    private static void UpdateResource(DrawableBrush.Resource resource, DrawableBrush drawableBrush)
    {
        bool updateOnly = false;
        resource.Update(drawableBrush, new CompositionContext(TimeSpan.Zero), ref updateOnly);
    }

    private static bool CanLock(WriteableBitmap bitmap)
    {
        try
        {
            using var _ = bitmap.Lock();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            AvaDispatcher.UIThread.RunJobs();
            if (stopwatch.Elapsed >= timeout)
                Assert.Fail($"Condition was not met within {timeout}.");

            await Task.Delay(10);
        }
    }
}

internal sealed partial class BlockingThumbnailDrawable(
    float width,
    float height,
    Brush.Resource? fill) : Drawable
{
    private readonly TaskCompletionSource<bool> _renderEntered =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _releaseRender =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource<bool> RenderEntered => _renderEntered;

    public void ReleaseRender() => _releaseRender.TrySetResult(true);

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        if (width > 0 && height > 0 && fill is not null)
        {
            context.DrawRectangle(
                new Rect(0, 0, width, height),
                fill,
                null);
        }

        _renderEntered.TrySetResult(true);
        _releaseRender.Task.GetAwaiter().GetResult();
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => new(width, height);

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }
}

internal sealed partial class DisposalTrackingDrawableBrush : DrawableBrush
{
    private int _resourceCreationThreadId;
    private int _resourceDisposalThreadId;
    private int _lastResourceUpdateThreadId;
    private int _resourceDisposeCalls;
    private int _resourceUpdateCalls;

    public int ResourceCreationThreadId => Volatile.Read(ref _resourceCreationThreadId);

    public int ResourceDisposalThreadId => Volatile.Read(ref _resourceDisposalThreadId);

    public int LastResourceUpdateThreadId => Volatile.Read(ref _lastResourceUpdateThreadId);

    public int ResourceDisposeCalls => Volatile.Read(ref _resourceDisposeCalls);

    public int ResourceUpdateCalls => Volatile.Read(ref _resourceUpdateCalls);

    public partial class Resource
    {
        private DisposalTrackingDrawableBrush? _owner;

        partial void PostUpdate(DisposalTrackingDrawableBrush obj, CompositionContext context)
        {
            _owner = obj;
            int updateCount = Interlocked.Increment(ref obj._resourceUpdateCalls);
            if (updateCount == 1)
            {
                Volatile.Write(
                    ref obj._resourceCreationThreadId,
                    Environment.CurrentManagedThreadId);
            }
            else
            {
                Volatile.Write(
                    ref obj._lastResourceUpdateThreadId,
                    Environment.CurrentManagedThreadId);
            }
        }

        partial void PostDispose(bool disposing)
        {
            if (disposing && _owner is not null)
            {
                Volatile.Write(
                    ref _owner._resourceDisposalThreadId,
                    Environment.CurrentManagedThreadId);
                Interlocked.Increment(ref _owner._resourceDisposeCalls);
            }
        }
    }
}
