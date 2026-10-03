using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Beutl.Editor.Components.ColorScopesTab.Views.Scopes;
using Beutl.Media.Source;
using BtlBitmap = Beutl.Media.Bitmap;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class ScopeControlLifetimeTests
{
    [AvaloniaTest]
    public async Task Superseded_render_is_discarded_before_the_next_render_starts()
    {
        using var source = Ref<BtlBitmap>.Create(new BtlBitmap(2, 2));
        var scope = CreateScope(source);
        var first = scope.QueueRender();
        var second = scope.QueueRender();
        try
        {
            scope.Refresh();
            await first.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            scope.Refresh();
            first.Release();
            await second.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(scope.CurrentBitmap, Is.Null,
                    "The superseded result must be discarded before releasing the render semaphore.");
                Assert.That(CanLock(first.Bitmap!), Is.False, "A discarded result must release its bitmap.");
            });

            second.Release();
            await WaitUntilAsync(() => source.RefCount == 1);
            Assert.That(scope.CurrentBitmap, Is.SameAs(second.Bitmap));
        }
        finally
        {
            first.Release();
            second.Release();
            await ClearAsync(scope, source);
        }
    }

    [AvaloniaTest]
    public async Task Clearing_the_source_during_a_render_cannot_repopulate_the_scope()
    {
        using var source = Ref<BtlBitmap>.Create(new BtlBitmap(2, 2));
        var scope = CreateScope(source);
        var render = scope.QueueRender();
        try
        {
            scope.Refresh();
            await render.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            scope.SourceBitmap = null;
            scope.Refresh();
            render.Release();
            await WaitUntilAsync(() => source.RefCount == 1);

            Assert.Multiple(() =>
            {
                Assert.That(scope.CurrentBitmap, Is.Null);
                Assert.That(CanLock(render.Bitmap!), Is.False);
            });
        }
        finally
        {
            render.Release();
            await ClearAsync(scope, source);
        }
    }

    [AvaloniaTest]
    public async Task Clearing_the_source_disposes_both_idle_buffers()
    {
        using var source = Ref<BtlBitmap>.Create(new BtlBitmap(2, 2));
        var scope = CreateScope(source);
        try
        {
            var first = await RenderAsync(scope, source);
            var second = await RenderAsync(scope, source);
            Assert.That(CanLock(first.Bitmap!), Is.True);
            Assert.That(scope.CurrentBitmap, Is.SameAs(second.Bitmap));

            scope.SourceBitmap = null;
            scope.Refresh();

            Assert.Multiple(() =>
            {
                Assert.That(scope.CurrentBitmap, Is.Null);
                Assert.That(CanLock(first.Bitmap!), Is.False);
                Assert.That(CanLock(second.Bitmap!), Is.False);
            });
        }
        finally
        {
            await ClearAsync(scope, source);
        }
    }

    [AvaloniaTest]
    public async Task Detaching_during_a_render_discards_its_result()
    {
        using var source = Ref<BtlBitmap>.Create(new BtlBitmap(2, 2));
        var scope = CreateScope(null);
        var window = new Window { Width = 48, Height = 48, Content = scope };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        scope.SourceBitmap = source;
        var render = scope.QueueRender();
        try
        {
            scope.Refresh();
            await render.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            window.Content = null;
            render.Release();
            await WaitUntilAsync(() => source.RefCount == 1);

            Assert.Multiple(() =>
            {
                Assert.That(scope.CurrentBitmap, Is.Null);
                Assert.That(CanLock(render.Bitmap!), Is.False);
            });
        }
        finally
        {
            render.Release();
            window.Close();
            await ClearAsync(scope, source);
        }
    }

    [AvaloniaTest]
    public async Task A_detached_control_ignores_refreshes_until_it_is_reattached()
    {
        using var source = Ref<BtlBitmap>.Create(new BtlBitmap(2, 2));
        var scope = CreateScope(null);
        var window = new Window { Width = 48, Height = 48, Content = scope };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        scope.SourceBitmap = source;
        window.Content = null;
        var render = scope.QueueRender();
        try
        {
            scope.Refresh();
            Assert.Multiple(() =>
            {
                Assert.That(source.RefCount, Is.EqualTo(1), "A queued refresh must not restart a detached control.");
                Assert.That(render.Entered.Task.IsCompleted, Is.False);
            });

            window.Content = scope;
            render.Release();
            await render.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => source.RefCount == 1);
            Assert.That(scope.CurrentBitmap, Is.SameAs(render.Bitmap));
        }
        finally
        {
            render.Release();
            window.Close();
            await ClearAsync(scope, source);
        }
    }

    [AvaloniaTest]
    public async Task Reattaching_renders_the_source_changed_while_detached_with_unchanged_bounds()
    {
        using var source = Ref<BtlBitmap>.Create(new BtlBitmap(2, 2));
        using var replacement = Ref<BtlBitmap>.Create(new BtlBitmap(2, 2));
        var scope = CreateScope(null);
        var window = new Window { Width = 48, Height = 48, Content = scope };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        scope.SourceBitmap = source;
        RenderRequest? reattachedRender = null;
        try
        {
            await RenderAsync(scope, source);
            Rect attachedBounds = scope.Bounds;
            window.Content = null;
            scope.SourceBitmap = replacement;
            scope.Refresh();
            reattachedRender = scope.QueueRender();
            reattachedRender.Release();

            window.Content = scope;
            await reattachedRender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => replacement.RefCount == 1);
            Assert.Multiple(() =>
            {
                Assert.That(scope.Bounds, Is.EqualTo(attachedBounds));
                Assert.That(reattachedRender.SourceBitmap, Is.SameAs(replacement.Value));
                Assert.That(scope.CurrentBitmap, Is.SameAs(reattachedRender.Bitmap));
            });
        }
        finally
        {
            reattachedRender?.Release();
            window.Close();
            await ClearAsync(scope, replacement);
        }
    }

    [AvaloniaTest]
    public async Task Detaching_keeps_a_worker_owned_buffer_alive_until_the_worker_finishes()
    {
        using var source = Ref<BtlBitmap>.Create(new BtlBitmap(2, 2));
        var scope = CreateScope(null);
        var window = new Window { Width = 48, Height = 48, Content = scope };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        scope.SourceBitmap = source;
        var render = scope.QueueRender();
        render.Release();
        try
        {
            scope.Refresh();
            await render.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => source.RefCount == 1);
            await RenderAsync(scope, source);
            var blockedRender = scope.QueueRender();
            render = blockedRender;
            scope.Refresh();
            await blockedRender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(blockedRender.ExistingBitmap, Is.Not.Null);

            window.Content = null;
            Assert.That(CanLock(blockedRender.Bitmap!), Is.True,
                "Detachment must not dispose a bitmap that RenderScope is still using.");
            blockedRender.Release();
            await WaitUntilAsync(() => source.RefCount == 1);

            Assert.Multiple(() =>
            {
                Assert.That(blockedRender.Failure, Is.Null);
                Assert.That(scope.CurrentBitmap, Is.Null);
                Assert.That(CanLock(blockedRender.Bitmap!), Is.False);
            });
        }
        finally
        {
            render.Release();
            window.Close();
            await ClearAsync(scope, source);
        }
    }

    [AvaloniaTest]
    public async Task A_queued_render_cannot_borrow_a_buffer_owned_by_its_predecessor()
    {
        using var source = Ref<BtlBitmap>.Create(new BtlBitmap(2, 2));
        var scope = CreateScope(source);
        RenderRequest? blockedRender = null;
        RenderRequest? queuedRender = null;
        try
        {
            var first = await RenderAsync(scope, source);
            var second = await RenderAsync(scope, source);
            blockedRender = scope.QueueRender();
            scope.Refresh();
            await blockedRender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(blockedRender.ExistingBitmap, Is.SameAs(first.Bitmap));

            queuedRender = scope.QueueRender();
            scope.Refresh();
            blockedRender.Release();
            await queuedRender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(scope.CurrentBitmap, Is.SameAs(second.Bitmap));
                Assert.That(queuedRender.ExistingBitmap, Is.Null,
                    "The predecessor owns the old back buffer until it discards it after cancellation.");
                Assert.That(CanLock(first.Bitmap!), Is.False);
            });

            queuedRender.Release();
            await WaitUntilAsync(() => source.RefCount == 1);
            Assert.That(scope.CurrentBitmap, Is.SameAs(queuedRender.Bitmap));
        }
        finally
        {
            blockedRender?.Release();
            queuedRender?.Release();
            await ClearAsync(scope, source);
        }
    }

    [AvaloniaTest]
    public async Task Replacing_and_disposing_the_source_keeps_the_running_read_alive()
    {
        var oldBitmap = new BtlBitmap(2, 2);
        using var source = Ref<BtlBitmap>.Create(oldBitmap);
        using var replacement = Ref<BtlBitmap>.Create(new BtlBitmap(2, 2));
        var scope = CreateScope(source);
        var first = scope.QueueRender();
        var second = scope.QueueRender();
        try
        {
            scope.Refresh();
            await first.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            scope.SourceBitmap = replacement;
            source.Dispose();
            Assert.That(oldBitmap.IsDisposed, Is.False, "The running request must hold its own source reference.");

            scope.Refresh();
            first.Release();
            await second.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(first.Failure, Is.Null);
                Assert.That(oldBitmap.IsDisposed, Is.True);
                Assert.That(second.SourceBitmap, Is.SameAs(replacement.Value));
            });

            second.Release();
            await WaitUntilAsync(() => replacement.RefCount == 1);
            Assert.That(scope.CurrentBitmap, Is.SameAs(second.Bitmap));
        }
        finally
        {
            first.Release();
            second.Release();
            await ClearAsync(scope, replacement);
        }
    }

    [AvaloniaTest]
    public async Task Clearing_releases_queued_source_references_without_interrupting_the_active_read()
    {
        var bitmap = new BtlBitmap(2, 2);
        using var source = Ref<BtlBitmap>.Create(bitmap);
        var scope = CreateScope(source);
        var active = scope.QueueRender();
        var queued = scope.QueueRender();
        try
        {
            scope.Refresh();
            await active.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            scope.Refresh();
            Assert.That(source.RefCount, Is.EqualTo(3));

            scope.SourceBitmap = null;
            scope.Refresh();
            await WaitUntilAsync(() => source.RefCount == 2);
            source.Dispose();
            Assert.Multiple(() =>
            {
                Assert.That(bitmap.IsDisposed, Is.False);
                Assert.That(queued.Entered.Task.IsCompleted, Is.False);
            });

            active.Release();
            await WaitUntilAsync(() => source.RefCount == 0);
            Assert.Multiple(() =>
            {
                Assert.That(active.Failure, Is.Null);
                Assert.That(bitmap.IsDisposed, Is.True);
                Assert.That(scope.CurrentBitmap, Is.Null);
                Assert.That(queued.Entered.Task.IsCompleted, Is.False);
            });
        }
        finally
        {
            active.Release();
            queued.Release();
            scope.SourceBitmap = null;
            scope.Refresh();
            await WaitUntilAsync(() => source.RefCount <= 1);
            scope.DisposeTestBitmaps();
        }
    }

    private static TestScope CreateScope(Ref<BtlBitmap>? source)
    {
        var scope = new TestScope { AxisMargin = 0 };
        scope.Measure(new Size(48, 48));
        scope.Arrange(new Rect(0, 0, 48, 48));
        scope.SourceBitmap = source;
        return scope;
    }

    private static async Task<RenderRequest> RenderAsync(TestScope scope, Ref<BtlBitmap> source)
    {
        var render = scope.QueueRender();
        render.Release();
        scope.Refresh();
        await render.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => source.RefCount == 1);
        return render;
    }

    private static async Task ClearAsync(TestScope scope, Ref<BtlBitmap> source)
    {
        scope.SourceBitmap = null;
        scope.Refresh();
        await WaitUntilAsync(() => source.RefCount == 1);
        scope.DisposeTestBitmaps();
    }

    private static bool CanLock(WriteableBitmap bitmap)
    {
        try
        {
            using var framebuffer = bitmap.Lock();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            Dispatcher.UIThread.RunJobs();
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(5))
                Assert.Fail("Timed out waiting for the background scope render to finish.");
            await Task.Delay(1);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class RenderRequest
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public WriteableBitmap? ExistingBitmap { get; set; }

        public BtlBitmap? SourceBitmap { get; set; }

        public WriteableBitmap? Bitmap { get; set; }

        public Exception? Failure { get; set; }

        public void Release() => _release.TrySetResult();

        public void WaitForRelease() => _release.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
    }

    private sealed class TestScope : ScopeControlBase
    {
        private readonly ConcurrentQueue<RenderRequest> _renders = new();
        private readonly ConcurrentBag<WriteableBitmap> _bitmaps = [];

        public WriteableBitmap? CurrentBitmap => RenderedBitmap;

        protected override string[]? VerticalAxisLabels => null;

        protected override string[]? HorizontalAxisLabels => null;

        public RenderRequest QueueRender()
        {
            var render = new RenderRequest();
            _renders.Enqueue(render);
            return render;
        }

        public void DisposeTestBitmaps()
        {
            foreach (var bitmap in _bitmaps)
                bitmap.Dispose();
        }

        protected override WriteableBitmap? RenderScope(
            BtlBitmap sourceBitmap, int targetWidth, int targetHeight, WriteableBitmap? existingBitmap)
        {
            if (!_renders.TryDequeue(out var render))
                throw new InvalidOperationException("Unexpected scope render.");

            var bitmap = existingBitmap ?? new WriteableBitmap(
                new PixelSize(targetWidth, targetHeight), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            if (existingBitmap == null)
                _bitmaps.Add(bitmap);
            render.ExistingBitmap = existingBitmap;
            render.SourceBitmap = sourceBitmap;
            render.Bitmap = bitmap;
            render.Entered.TrySetResult();
            try
            {
                render.WaitForRelease();
                sourceBitmap.ThrowIfDisposed();
                using var framebuffer = bitmap.Lock();
                return bitmap;
            }
            catch (Exception ex)
            {
                render.Failure = ex;
                throw;
            }
        }
    }
}
