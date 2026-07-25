using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reactive.Subjects;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Threading;
using AvaDispatcher = Avalonia.Threading.Dispatcher;
using AvaImageBrush = Avalonia.Media.ImageBrush;
using AvaPixelSize = Avalonia.PixelSize;
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

            Assert.Multiple(() =>
            {
                Assert.That(imageBrush.Stretch, Is.EqualTo(AvaStretch.None));
                Assert.That(imageBrush.Source, Is.Not.SameAs(first));
                Assert.That(
                    ((WriteableBitmap)imageBrush.Source!).PixelSize,
                    Is.EqualTo(new AvaPixelSize(72, 36)));
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
