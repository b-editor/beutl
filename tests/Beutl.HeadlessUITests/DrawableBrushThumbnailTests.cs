using System.Diagnostics;
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
        var publishedSizes = new List<AvaPixelSize>();
        imageBrush.PropertyChanged += (_, args) =>
        {
            if (args.Property == AvaImageBrush.SourceProperty
                && imageBrush.Source is WriteableBitmap bitmap)
            {
                publishedSizes.Add(bitmap.PixelSize);
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

            Assert.That(publishedSizes, Is.EqualTo(new[] { new AvaPixelSize(72, 36) }));
        }
        finally
        {
            staleDrawable.ReleaseRender();
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
