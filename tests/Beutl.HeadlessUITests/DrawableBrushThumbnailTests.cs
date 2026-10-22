using System.Collections.Concurrent;
using System.Reactive.Subjects;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Beutl.Composition;
using Beutl.Editor.Components.Helpers;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Testing.Headless;
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
        TestGpu.RequireAnyDevice();
        AvaImageBrush imageBrush;
        await using (var scope = new ThumbnailScope(CreateRectangle(40, 24, Colors.Red), Stretch.Uniform))
        {
            imageBrush = scope.ImageBrush;
            WriteableBitmap first = await scope.PublishFirstAsync();
            uint initialPixel = SamplePixel(first, 20, 12);
            Assert.That(imageBrush.Stretch, Is.EqualTo(AvaStretch.Uniform));

            scope.DrawableBrush.Drawable.Value = CreateRectangle(72, 36, Colors.Blue);
            scope.DrawableBrush.Stretch.Value = Stretch.None;
            UpdateResource(scope.Resource, scope.DrawableBrush);
            scope.Handler.Update();

            await HeadlessTestHelpers.WaitUntilAsync(
                () => imageBrush.Source is WriteableBitmap bitmap
                      && !ReferenceEquals(bitmap, first)
                      && bitmap.PixelSize == new AvaPixelSize(72, 36));
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
        TestGpu.RequireAnyDevice();
        var staleDrawable = new BlockingThumbnailDrawable(40, 24, Brushes.Resource.Red);
        var imageBrush = new AvaImageBrush();
        ConcurrentQueue<AvaPixelSize> publishedSizes = RecordPublishedSizes(imageBrush);
        await using var scope = new ThumbnailScope(staleDrawable, imageBrush: imageBrush);

        try
        {
            scope.Handler.Update();
            await staleDrawable.RenderEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            scope.DrawableBrush.Drawable.Value = CreateRectangle(72, 36, Colors.Blue);
            UpdateResource(scope.Resource, scope.DrawableBrush);
            scope.Handler.Update();
            staleDrawable.ReleaseRender();

            await HeadlessTestHelpers.WaitUntilAsync(
                () => imageBrush.Source is WriteableBitmap bitmap
                      && bitmap.PixelSize == new AvaPixelSize(72, 36));

            Assert.That(publishedSizes.ToArray(), Is.EqualTo(new[] { new AvaPixelSize(72, 36) }));
        }
        finally
        {
            staleDrawable.ReleaseRender();
        }
    }

    [AvaloniaTest]
    public async Task Superseded_queued_publication_is_discarded_before_ui_delivery()
    {
        TestGpu.RequireAnyDevice();
        var imageBrush = new AvaImageBrush();
        ConcurrentQueue<AvaPixelSize> publishedSizes = RecordPublishedSizes(imageBrush);
        await using var scope = new ThumbnailScope(CreateRectangle(40, 24, Colors.Red), imageBrush: imageBrush);

        Assert.That(AvaDispatcher.UIThread.CheckAccess(), Is.True);
        scope.Handler.Update();
        RenderThread.Dispatcher.Invoke(
            static () => { },
            DispatchPriority.Low,
            CancellationToken.None);

        scope.DrawableBrush.Drawable.Value = CreateRectangle(72, 36, Colors.Blue);
        UpdateResource(scope.Resource, scope.DrawableBrush);
        scope.Handler.Update();

        await HeadlessTestHelpers.WaitUntilAsync(
            () => imageBrush.Source is WriteableBitmap bitmap
                  && bitmap.PixelSize == new AvaPixelSize(72, 36));

        Assert.That(
            publishedSizes.ToArray(),
            Is.EqualTo(new[] { new AvaPixelSize(72, 36) }));
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
            using var clock = new BehaviorSubject<MediaTime>(MediaTime.Zero);
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
        using var clock = new BehaviorSubject<MediaTime>(MediaTime.Zero);
        (_, IDisposable subscription, _) = drawableBrush.ToAvaBrushSync(clock);

        await HeadlessTestHelpers.WaitUntilAsync(
            () => drawableBrush.ResourceUpdateCalls == 1);

        clock.OnNext(MediaTime.FromSeconds(1));
        await HeadlessTestHelpers.WaitUntilAsync(
            () => drawableBrush.ResourceUpdateCalls == 2);

        subscription.Dispose();

        await HeadlessTestHelpers.WaitUntilAsync(
            () => drawableBrush.ResourceDisposeCalls == 1);
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
    public async Task Grouped_content_publishes_a_thumbnail()
    {
        TestGpu.RequireAnyDevice();
        var group = new DrawableGroup();
        group.Children.Add(CreateRectangle(40, 24, Colors.Red));
        await using var scope = new ThumbnailScope(group, Stretch.Uniform);

        scope.Handler.Update();
        await HeadlessTestHelpers.WaitUntilAsync(() => scope.ImageBrush.Source is WriteableBitmap);
    }

    private static RectShape CreateRectangle(float width, float height, Color color)
    {
        return new RectShape
        {
            Width = { Value = width },
            Height = { Value = height },
            Fill = { Value = new SolidColorBrush(color) },
        };
    }

    private static void UpdateResource(DrawableBrush.Resource resource, DrawableBrush drawableBrush)
    {
        bool versionBumped = false;
        resource.Reconcile(drawableBrush, new CompositionContext(MediaTime.Zero), ref versionBumped);
    }

    private static ConcurrentQueue<AvaPixelSize> RecordPublishedSizes(AvaImageBrush imageBrush)
    {
        var publishedSizes = new ConcurrentQueue<AvaPixelSize>();
        imageBrush.PropertyChanged += (_, args) =>
        {
            if (args.Property == AvaImageBrush.SourceProperty
                && imageBrush.Source is WriteableBitmap bitmap)
            {
                publishedSizes.Enqueue(bitmap.PixelSize);
            }
        };
        return publishedSizes;
    }

    // The handler does not own the resource; release it on the render thread after the queued updates.
    private static Task ReleaseResourceAsync(DrawableBrush.Resource resource)
        => RenderThread.Dispatcher.InvokeAsync(resource.Dispose, DispatchPriority.Low);

    // A drawable brush, its resource, and a handler that publishes thumbnails of it to an image brush.
    private sealed class ThumbnailScope : IAsyncDisposable
    {
        public ThumbnailScope(Drawable drawable, Stretch? stretch = null, AvaImageBrush? imageBrush = null)
        {
            DrawableBrush = new DrawableBrush(drawable);
            if (stretch is { } value)
                DrawableBrush.Stretch.Value = value;
            Resource = (DrawableBrush.Resource)DrawableBrush.ToResource(new CompositionContext(MediaTime.Zero));
            ImageBrush = imageBrush ?? new AvaImageBrush();
            Handler = new AvaloniaTypeConverter.DrawableImageBrushHandler(Resource, ImageBrush, RenderThread.Dispatcher);
        }

        public DrawableBrush DrawableBrush { get; }

        public DrawableBrush.Resource Resource { get; }

        public AvaImageBrush ImageBrush { get; }

        public AvaloniaTypeConverter.DrawableImageBrushHandler Handler { get; }

        // Publishes the 40x24 thumbnail the tests start from.
        public async Task<WriteableBitmap> PublishFirstAsync()
        {
            Handler.Update();
            await HeadlessTestHelpers.WaitUntilAsync(
                () => ImageBrush.Source is WriteableBitmap bitmap
                      && bitmap.PixelSize == new AvaPixelSize(40, 24));
            return (WriteableBitmap)ImageBrush.Source!;
        }

        public async ValueTask DisposeAsync()
        {
            Handler.Dispose();
            await ReleaseResourceAsync(Resource);
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

        partial void PostReconcile(DisposalTrackingDrawableBrush obj, CompositionContext context)
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
