using System.Collections.Immutable;
using System.Diagnostics;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Moq;
using Reactive.Bindings;
using Vector2 = System.Numerics.Vector2;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class TimelineVirtualizationTests
{
    [AvaloniaTest]
    public void Switching_context_keeps_overlays_and_releases_compact_clips()
    {
        var scene = new Scene();
        scene.Children.Add(Clip(0, 0));
        using var host = new ViewportHost(scene, scale: 0.1f);
        Control overlay = host.View.FindControl<Control>("overlay")!;
        Assert.That(host.Panel.Children.OfType<CompactElementLayer>(), Is.Not.Empty);
        host.View.DataContext = null;
        Assert.That(host.Panel.Children, Does.Contain(overlay));
        Assert.That(host.Panel.Children.OfType<CompactElementLayer>(), Is.Empty);
        host.View.DataContext = host.Model;
        host.Model.Options.Value = host.Model.Options.Value with { Scale = 1 };
        HeadlessTestHelpers.Render(3);
        Assert.That(host.Panel.Children, Does.Contain(overlay));
        Assert.That(host.Views.Single().DataContext, Is.SameAs(host.Model.Elements.Single()));
    }

    [AvaloniaTest]
    public void Compact_runs_preserve_overlapping_scene_order_and_selection_order()
    {
        var scene = new Scene();
        Element first = Clip(0, 0);
        Element wide = Clip(0, 0);
        Element last = Clip(0, 0);
        first.Length.Value = last.Length.Value = 12.5.PixelToTime(1);
        scene.Children.AddRange([first, wide, last]);
        using var host = new ViewportHost(scene);
        host.Selection.Select(wide);
        HeadlessTestHelpers.Render(3);
        Point point = host.Panel.TranslatePoint(new Point(5, 12.5), host.Window)!.Value;
        host.Window.MouseMove(point);
        HeadlessTestHelpers.Render();
        Assert.That(host.Views.Select(v => ((ElementViewModel)v.DataContext!).Model), Is.EqualTo(new[] { wide }));
        host.Selection.SelectedElements.Clear();
        HeadlessTestHelpers.Render();
        host.Window.MouseMove(point + new Vector(1, 0));
        HeadlessTestHelpers.Render(3);
        Assert.That(host.Views.Select(v => ((ElementViewModel)v.DataContext!).Model), Is.EqualTo(new[] { wide, last }));
        host.Model.Options.Value = host.Model.Options.Value with { Scale = 2 };
        HeadlessTestHelpers.Render(3);
        Assert.That(host.Views.Select(v => ((ElementViewModel)v.DataContext!).Model), Is.EqualTo(new[] { first, wide, last }));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Narrow_clip_flyout_keeps_its_anchor_when_scrolling(bool template)
    {
        var scene = new Scene { Duration = { Value = MediaTime.FromSeconds(100) } };
        Element first = Clip(0, 0);
        scene.Children.AddRange([first, Clip(0, 50)]);
        using var host = new ViewportHost(scene, scale: 0.1f);
        host.Window.MouseMove(host.Panel.TranslatePoint(new Point(11, 12.5), host.Window)!.Value);
        HeadlessTestHelpers.Render(3);
        ElementView view = host.Views.Single();
        object model = view.DataContext!;
        const System.Reflection.BindingFlags instance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(ElementView).GetMethod(template ? "SaveAsTemplate_Click" : "ChangeColor_Click", instance)!
            .Invoke(view, [null, new Avalonia.Interactivity.RoutedEventArgs()]);
        try
        {
            HeadlessTestHelpers.Render(3);
            // The color picker and the template flyout both show a picker presenter.
            Popup popup = host.Window.OpenedPopups.Single(open => open.Child is FluentAvalonia.UI.Controls.FAPickerFlyoutPresenter);
            Assert.That(popup.IsOpen, Is.True);
            host.Scroll(MediaTime.FromSeconds(50).TimeToPixel(0.1f), 0);
            Assert.That(popup.IsOpen, Is.True);
            Assert.That(view.DataContext, Is.SameAs(model));
        }
        finally
        {
            foreach (Popup open in host.Window.OpenedPopups.ToArray()) open.Close();
        }
    }

    [AvaloniaTest]
    public void Twenty_thousand_grouped_clips_can_all_fit_in_the_viewport()
    {
        var scene = new Scene { Duration = { Value = MediaTime.FromSeconds(20_010) } };
        for (int i = 0; i < 10_000; i++)
        {
            Element first = Clip(0, i * 2);
            Element second = Clip(1, i * 2);
            scene.Children.AddRange([first, second]);
            scene.Groups.Add(ImmutableHashSet.Create(first.Id.Value, second.Id.Value));
        }
        var timer = Stopwatch.StartNew();
        using var host = new ViewportHost(scene, scale: 0.0002f);
        double initialMs = timer.Elapsed.TotalMilliseconds;
        Assert.That(host.Views, Is.Empty);
        Assert.That(host.Panel.Children.OfType<CompactElementLayer>().Count(), Is.EqualTo(2));
        Assert.That(host.Panel.Children.OfType<CompactElementLayer>().Sum(layer => layer.ElementCount), Is.EqualTo(20_000));
        Assert.That(host.Model.GetViewModelFor(scene.Children[^1])!.GetGroupOrSelectedElements()
            .Select(vm => vm.Model), Is.EquivalentTo(scene.Children.TakeLast(2)));
        timer.Restart();
        host.Selection.SelectedElements.AddRange(scene.Children);
        HeadlessTestHelpers.Render(3);
        double selectMs = timer.Elapsed.TotalMilliseconds;
        Assert.That(host.Views, Is.Empty, "Selecting all must not construct all editing controls.");
        Assert.That(host.Model.SelectedElements, Has.Count.EqualTo(20_000));
        HeadlessCapture.SaveIfRequested(host.Window, nameof(TimelineVirtualizationTests), "20000-overview-selected");
        host.Selection.SelectedElements.Clear();
        HeadlessTestHelpers.Render();
        HeadlessCapture.SaveIfRequested(host.Window, nameof(TimelineVirtualizationTests), "20000-overview");
        timer.Restart();
        host.Model.Options.Value = host.Model.Options.Value with { Scale = 1 };
        HeadlessTestHelpers.Render(3);
        double zoomMs = timer.Elapsed.TotalMilliseconds;
        AssertVisibleClips(host);
        Assert.That(host.Views, Has.Length.LessThan(10));
        Assert.That(host.Panel.Children.OfType<CompactElementLayer>(), Is.Empty);
        TestContext.Out.WriteLine($"20,000 overview clips: initial {initialMs:F1} ms, select all {selectMs:F1} ms, zoom {zoomMs:F1} ms.");
    }

    [AvaloniaTest]
    [TestCase(2, 10_000)]
    [TestCase(200, 100)]
    [TestCase(20_000, 1)]
    public void Twenty_thousand_clips_only_realize_the_viewport(int rows, int columns)
    {
        var scene = new Scene { Duration = { Value = MediaTime.FromSeconds(columns * 2 + 10) } };
        scene.Children.AddRange(Enumerable.Range(0, rows * columns).Select(i =>
            Clip(i / columns, i % columns * 2 + (i / columns % 3) * 0.25)));
        var timer = Stopwatch.StartNew();
        using var host = new ViewportHost(scene);
        double initialMs = timer.Elapsed.TotalMilliseconds;
        AssertVisibleClips(host);

        int scaleNotifications = 0;
        using IDisposable scale = host.Model.Scale.Skip(1).Subscribe(_ => scaleNotifications++);
        var samples = new List<double>();
        for (int i = 0; i < 5; i++)
        {
            double fraction = i / 5d;
            host.Scroll(MediaTime.FromSeconds((columns - 1) * 2 * fraction).TimeToPixel(1),
                host.Model.CalculateLayerTop(rows - 1) * fraction);
            AssertVisibleClips(host);
        }

        double startX = host.ScrollViewer.Offset.X;
        double startY = host.ScrollViewer.Offset.Y;
        for (int i = 0; i < 20; i++)
        {
            timer.Restart();
            host.Scroll(startX + i * 12, startY + (rows > 2 ? i * 12 : 0));
            samples.Add(timer.Elapsed.TotalMilliseconds);
            AssertVisibleClips(host);
        }

        Assert.That(scaleNotifications, Is.Zero, "Scrolling must not recompute every clip's geometry/thumbnails.");
        // A selection belongs to the editor, including the clips with no control.
        timer.Restart();
        host.Selection.SelectedElements.AddRange(scene.Children);
        HeadlessTestHelpers.Render();
        Assert.That(host.Model.SelectedElements, Has.Count.EqualTo(20_000));
        AssertVisibleClips(host);
        host.Selection.SelectedElements.Clear();
        double selectionMs = timer.Elapsed.TotalMilliseconds;

        timer.Restart();
        foreach (Element element in scene.Children.Take(200))
            element.Length.Value += MediaTime.FromSeconds(0.1);
        HeadlessTestHelpers.Render(3);
        double editMs = timer.Elapsed.TotalMilliseconds;
        AssertVisibleClips(host);

        host.Scroll(MediaTime.FromSeconds((columns - 1) * 2).TimeToPixel(1), host.Model.CalculateLayerTop(rows - 1));
        Assert.That(host.Views.Select(v => ((ElementViewModel)v.DataContext!).Model), Does.Contain(scene.Children[^1]));
        HeadlessCapture.SaveIfRequested(host.Window, nameof(TimelineVirtualizationTests), $"{rows}-rows-{columns}-clips");
        samples.Sort();
        TestContext.Out.WriteLine($"20,000 clips ({rows} rows): initial {initialMs:F1} ms, "
            + $"scroll median {samples[samples.Count / 2]:F1} ms, p95 {samples[^2]:F1} ms, "
            + $"select/clear all {selectionMs:F1} ms, "
            + $"200 edits {editMs:F1} ms, "
            + $"realized {host.Views.Length} clips / {host.View.GetVisualDescendants().OfType<LayerHeader>().Count()} headers.");
    }

    [AvaloniaTest]
    public void Recycling_preserves_selection_order_and_tracks_edits_and_scopes()
    {
        var scene = new Scene { Duration = { Value = MediaTime.FromSeconds(100) } };
        Element first = Clip(0, 0);
        Element second = Clip(0, 0);
        Element distant = Clip(30, 50);
        var portal = new PortalObject { Count = { Value = 35 } };
        first.Objects.Add(portal);
        scene.Children.AddRange([first, second, distant]);
        using var host = new ViewportHost(scene);
        host.Selection.Select(second);
        host.Scroll(MediaTime.FromSeconds(50).TimeToPixel(1), host.Model.CalculateLayerTop(30));
        Assert.That(host.Views.Select(v => ((ElementViewModel)v.DataContext!).Model), Is.EqualTo(new[] { distant }));

        host.Scroll(0, 0);
        Assert.That(host.Views.Select(v => ((ElementViewModel)v.DataContext!).Model), Is.EqualTo(new[] { first, second }));
        Assert.That(host.Model.GetViewModelFor(second)!.IsSelected.Value, Is.True);
        host.Scroll(0, host.Model.CalculateLayerTop(20));
        Assert.That(host.Views, Is.Empty);
        Assert.That(host.Panel.Children.OfType<ElementScopeView>().Single().DataContext,
            Is.SameAs(host.Model.GetViewModelFor(first)!.Scope), "A scope can be visible while its owner is offscreen.");

        portal.Count.Value = 1;
        distant.Start.Value = MediaTime.Zero;
        distant.ZIndex.Value = 20;
        HeadlessTestHelpers.Render();
        Assert.That(host.Views.Single().DataContext, Is.SameAs(host.Model.GetViewModelFor(distant)));
        Assert.That(host.Panel.Children.OfType<ElementScopeView>(), Is.Empty);

        // Preview geometry is not yet committed to the scene, but it must enter and leave the viewport.
        ElementViewModel preview = host.Model.GetViewModelFor(second)!;
        preview.Margin.Value = new Thickness(0, host.Model.CalculateLayerTop(20), 0, 0);
        HeadlessTestHelpers.Render();
        Assert.That(host.Views.Select(v => v.DataContext), Does.Contain(preview));
        scene.Children.Remove(second);
        HeadlessTestHelpers.Render();
        Assert.That(host.Model.GetViewModelFor(second), Is.Null);
        Assert.That(host.Views.Select(v => v.DataContext), Does.Not.Contain(preview));
        scene.Children.Clear();
        HeadlessTestHelpers.Render();
        Assert.That(host.Views, Is.Empty);
        Assert.That(host.Panel.Children.OfType<ElementScopeView>(), Is.Empty);
    }

    [AvaloniaTest]
    public async Task Captured_clip_survives_scrolling_and_can_be_moved_and_undone()
    {
        // The narrow clip comes first in scene order, so its compact layers precede the captured view
        // until the scroll below leaves them behind.
        using TimelineTestHost host = await TimelineTestHost.CreateAsync("virtualized-drag", MediaTime.FromSeconds(100),
            [new ElementDescription(MediaTime.Zero, MediaTime.FromSeconds(0.1), 1,
                new ElementSource.EngineObject(() => new RectShape())),
             new ElementDescription(MediaTime.Zero, MediaTime.FromSeconds(2), 0,
                new ElementSource.EngineObject(() => new RectShape()))]);
        ElementViewModel model = host.Model.Elements[1];
        Assert.That(host.Timeline.Children.OfType<CompactElementLayer>(), Is.Not.Empty);
        ElementView view = host.Timeline.Children.OfType<ElementView>().Single();
        int detached = 0;
        view.DetachedFromLogicalTree += (_, _) => detached++;
        Border border = view.FindControl<Border>("border")!;
        Point press = border.TranslatePoint(new Point(50, 12), host.Window)!.Value;
        TimelineTestHost.WaitForHitTest(host.Window, border, press);
        host.Window.MouseDown(press, MouseButton.Left);
        host.Model.Options.Value = host.Model.Options.Value with { Offset = new Vector2(2_000, 0) };
        HeadlessTestHelpers.Render();
        Assert.That(host.Timeline.Children, Does.Contain(view));
        Assert.That(detached, Is.Zero, "Detaching the captured view ends its drag.");

        Point release = press + new Vector(50, 0);
        host.Window.MouseMove(release, RawInputModifiers.LeftMouseButton);
        host.Window.MouseUp(release, MouseButton.Left);
        HeadlessTestHelpers.Render(5);
        Assert.That(model.Model.Start.Value, Is.GreaterThan(MediaTime.Zero));
        Assert.That(await ((EditViewModel)host.Model.EditorContext).UndoAsync(), Is.True);
        host.Model.Options.Value = host.Model.Options.Value with { Offset = Vector2.Zero };
        HeadlessTestHelpers.Render(5);
        Assert.That(model.Model.Start.Value, Is.EqualTo(MediaTime.Zero));
        Assert.That(host.Timeline.Children.OfType<ElementView>().Single().DataContext, Is.SameAs(model));
    }

    private static void AssertVisibleClips(ViewportHost host)
    {
        ElementView[] views = host.Views;
        Assert.That(views.Length, Is.InRange(1, 250));
        Assert.That(host.View.GetVisualDescendants().OfType<LayerHeader>().Count(), Is.LessThan(100));
        var visible = views.Select(v => v.DataContext).ToHashSet();
        var viewport = new Rect(host.ScrollViewer.Offset.X, host.ScrollViewer.Offset.Y,
            host.ScrollViewer.Viewport.Width, host.ScrollViewer.Viewport.Height);
        foreach (ElementViewModel model in host.Model.Elements)
        {
            var bounds = new Rect(model.BorderMargin.Value.Left, model.Margin.Value.Top,
                model.Width.Value, FrameNumberHelper.LayerHeight);
            if (bounds.Intersects(viewport)) Assert.That(visible.Contains(model), Is.True, model.Name.Value);
        }
    }

    [AvaloniaTest]
    public async Task Offscreen_thumbnails_wait_until_the_clip_is_realized_and_reload_after_recycling()
    {
        var scene = new Scene { Duration = { Value = MediaTime.FromSeconds(100) } };
        Element first = Clip(0, 0);
        Element distant = Clip(0, 50);
        var firstSource = new CountingThumbnailSource();
        var distantSource = new CountingThumbnailSource();
        first.Objects.Add(firstSource);
        distant.Objects.Add(distantSource);
        scene.Children.AddRange([first, distant]);
        using var host = new ViewportHost(scene);
        Assert.That(firstSource.Requests, Is.GreaterThan(0));
        Assert.That(distantSource.Requests, Is.Zero);

        PreviewSourceMode originalMode = GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode.Value;
        int initialRequests = firstSource.Requests;
        try
        {
            GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode.Value = originalMode == PreviewSourceMode.PreferProxy
                ? PreviewSourceMode.ForceOriginal : PreviewSourceMode.PreferProxy;
            HeadlessTestHelpers.Render();
            Assert.That(firstSource.Requests, Is.GreaterThan(initialRequests));
            Assert.That(distantSource.Requests, Is.Zero);
        }
        finally
        {
            GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode.Value = originalMode;
            HeadlessTestHelpers.Render();
        }

        host.Scroll(MediaTime.FromSeconds(50).TimeToPixel(1), 0);
        Assert.That(distantSource.Requests, Is.GreaterThan(0));
        int firstRequests = firstSource.Requests;
        firstSource.Invalidate();
        await Task.Delay(550);
        HeadlessTestHelpers.Render();
        Assert.That(firstSource.Requests, Is.EqualTo(firstRequests));

        host.Scroll(0, 0);
        Assert.That(firstSource.Requests, Is.GreaterThan(firstRequests));
    }

    [AvaloniaTest]
    [TestCase(ThumbnailsKind.Video)]
    [TestCase(ThumbnailsKind.Audio)]
    public async Task Narrow_clips_skip_thumbnails_and_resume_after_zoom_or_resize(ThumbnailsKind kind)
    {
        var scene = new Scene();
        Element element = Clip(0, 0);
        element.Length.Value = 12.5.PixelToTime(1);
        var source = new CountingThumbnailSource(kind);
        element.Objects.Add(source);
        scene.Children.Add(element);
        using var host = new ViewportHost(scene);
        ElementViewModel model = host.Model.GetViewModelFor(element)!;
        model.InvalidateAndUpdateThumbnails();
        model.OnVisibleRangeChanged(0, 0);
        await Task.Delay(550);
        HeadlessTestHelpers.Render();
        Assert.That(source.Requests, Is.Zero, "A realized narrow clip must not start decoding.");
        Assert.That(model.ThumbnailsKind.Value, Is.EqualTo(ThumbnailsKind.None));
        Assert.That(model.IsThumbnailsDisabled.Value, Is.False, "Width must not change the user's setting.");
        HeadlessCapture.SaveIfRequested(host.Window, nameof(TimelineVirtualizationTests), $"{kind}-narrow");

        host.Model.Options.Value = host.Model.Options.Value with { Scale = 2 };
        await HeadlessTestHelpers.WaitUntilAsync(() => source.Requests > 0 && HasThumbnails());
        Assert.That(model.Width.Value, Is.EqualTo(25).Within(0.001));
        Assert.That(model.ThumbnailsKind.Value, Is.EqualTo(kind));
        HeadlessTestHelpers.Render();
        HeadlessCapture.SaveIfRequested(host.Window, nameof(TimelineVirtualizationTests), $"{kind}-minimum-width");

        int requests = source.Requests;
        source.HoldRequests = true;
        if (kind == ThumbnailsKind.Video)
        {
            host.Views.Single().FindControl<ThumbnailStripControl>("thumbnailStrip")!.ClearThumbnails();
            model.OnVisibleRangeChanged(0, 0);
        }
        else model.InvalidateAndUpdateThumbnails();
        await HeadlessTestHelpers.WaitUntilAsync(() => source.Requests > requests);
        CancellationToken pending = source.LastRequestCancellation;

        element.Length.Value = 24.5.PixelToTime(2);
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(pending.IsCancellationRequested, Is.True, "Shrinking cancels work without waiting for the debounce.");
            Assert.That(model.ThumbnailsKind.Value, Is.EqualTo(ThumbnailsKind.None));
            Assert.That(model.VideoThumbnailCount.Value, Is.Zero);
            Assert.That(model.WaveformChunkCount.Value, Is.Zero);
        });
        requests = source.Requests;
        source.Invalidate();
        model.OnVisibleRangeChanged(0, 0);
        model.InvalidateAndUpdateThumbnails();
        await Task.Delay(550);
        HeadlessTestHelpers.Render();
        Assert.That(source.Requests, Is.EqualTo(requests), "Queued scroll or invalidation events cannot restart a narrow clip.");

        source.HoldRequests = false;
        element.Length.Value = 25.0.PixelToTime(2);
        await HeadlessTestHelpers.WaitUntilAsync(() => source.Requests > requests && HasThumbnails());
        model.IsThumbnailsDisabled.Value = true;
        requests = source.Requests;
        host.Model.Options.Value = host.Model.Options.Value with { Scale = 1 };
        element.Length.Value = 75.0.PixelToTime(1);
        await Task.Delay(550);
        HeadlessTestHelpers.Render();
        Assert.That(model.Width.Value, Is.EqualTo(75).Within(0.001));
        Assert.That(source.Requests, Is.EqualTo(requests), "Zooming or resizing must respect the explicit thumbnail setting.");
        Assert.That(host.Model.ThumbnailsDisabledElements, Does.Contain(element.Id.Value));
        model.IsThumbnailsDisabled.Value = false;
        await HeadlessTestHelpers.WaitUntilAsync(() => source.Requests > requests && HasThumbnails());
        HeadlessTestHelpers.Render();
        HeadlessCapture.SaveIfRequested(host.Window, nameof(TimelineVirtualizationTests), $"{kind}-restored");

        bool HasThumbnails() => kind == ThumbnailsKind.Video
            ? model.VideoThumbnailCount.Value > 0 : model.WaveformChunkCount.Value > 0;
    }

    private sealed class CountingThumbnailSource(ThumbnailsKind kind = ThumbnailsKind.Audio) : EngineObject, IThumbnailsProvider
    {
        public int Requests { get; private set; }
        public ThumbnailsKind ThumbnailsKind => kind;
        public bool HoldRequests { get; set; }
        public CancellationToken LastRequestCancellation { get; private set; }
        public event EventHandler? ThumbnailsInvalidated;
        public void Invalidate() => ThumbnailsInvalidated?.Invoke(this, EventArgs.Empty);

        private Task BeginRequest(CancellationToken cancellationToken)
        {
            Requests++;
            LastRequestCancellation = cancellationToken;
            return HoldRequests ? Task.Delay(Timeout.Infinite, cancellationToken) : Task.CompletedTask;
        }

        public async IAsyncEnumerable<WaveformChunk> GetWaveformChunksAsync(int chunkCount, int samplesPerChunk,
            IThumbnailCacheService? cacheService,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await BeginRequest(cancellationToken);
            for (int i = 0; i < chunkCount; i++)
                yield return new WaveformChunk(i, -0.5f, 0.5f);
        }

        public async IAsyncEnumerable<(int Index, int Count, Beutl.Media.Bitmap Thumbnail)> GetThumbnailStripAsync(
            int maxWidth, int maxHeight, IThumbnailCacheService? cacheService = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default,
            int startIndex = 0, int endIndex = -1)
        {
            await BeginRequest(cancellationToken);
            var bitmap = new Beutl.Media.Bitmap(4, 4);
            bitmap.GetPixelSpan<Beutl.Media.Pixel.Bgra8888>().Fill(new Beutl.Media.Pixel.Bgra8888(180, 100, 40, 255));
            yield return (0, 1, bitmap);
        }
    }

    private static Element Clip(int row, double seconds) => new()
    {
        ZIndex = { Value = row },
        Start = { Value = MediaTime.FromSeconds(seconds) },
        Length = { Value = MediaTime.FromSeconds(1.5) },
        Name = { Value = $"Clip {row} / {seconds}" }
    };

    // Real timeline services and controls, without writing 20,000 element files or starting a player.
    private sealed class ViewportHost : IDisposable
    {
        private readonly TimelineOptionsProviderImpl _options;
        private readonly EditorClockImpl _clock;
        public ViewportHost(Scene scene, float scale = 1)
        {
            _options = new TimelineOptionsProviderImpl(scene);
            _options.Options.Value = _options.Options.Value with { Scale = scale };
            _clock = new EditorClockImpl(scene);
            Selection = new EditorSelectionImpl();
            var buffer = new Mock<IBufferStatus>();
            buffer.SetupGet(x => x.Start).Returns(new ReactivePropertySlim<double>());
            buffer.SetupGet(x => x.End).Returns(new ReactivePropertySlim<double>());
            buffer.SetupGet(x => x.CacheBlocks).Returns(new ReactivePropertySlim<CacheBlock[]>([]));
            var context = new Mock<IEditorContext>();
            context.Setup(x => x.GetService(typeof(ITimelineOptionsProvider))).Returns(_options);
            context.Setup(x => x.GetService(typeof(IEditorClock))).Returns(_clock);
            context.Setup(x => x.GetService(typeof(IEditorSelection))).Returns(Selection);
            context.Setup(x => x.GetService(typeof(IBufferStatus))).Returns(buffer.Object);
            Model = new TimelineTabViewModel(context.Object);
            View = new TimelineTabView { DataContext = Model };
            Window = new Window { Content = View, Width = 960, Height = 420 };
            Window.Show();
            HeadlessTestHelpers.Render(3);
            Panel = View.FindControl<Panel>("TimelinePanel")!;
            ScrollViewer = View.FindControl<ScrollViewer>("ContentScroll")!;
        }

        public TimelineTabViewModel Model { get; }
        public EditorSelectionImpl Selection { get; }
        public TimelineTabView View { get; }
        public Window Window { get; }
        public Panel Panel { get; }
        public ScrollViewer ScrollViewer { get; }
        public ElementView[] Views => Panel.Children.OfType<ElementView>().ToArray();

        public void Scroll(double x, double y)
        {
            Model.Options.Value = Model.Options.Value with { Offset = new Vector2((float)x, (float)y) };
            HeadlessTestHelpers.Render(3);
        }

        public void Dispose()
        {
            int count = Model.Elements.Count;
            var timer = Stopwatch.StartNew();
            View.DataContext = null;
            Window.Close();
            double viewMs = timer.Elapsed.TotalMilliseconds;
            timer.Restart();
            Model.Dispose();
            double modelMs = timer.Elapsed.TotalMilliseconds;
            Selection.Dispose();
            _clock.Dispose();
            _options.Dispose();
            if (count >= 20_000)
                TestContext.Out.WriteLine($"Close: view {viewMs:F1} ms, model {modelMs:F1} ms.");
        }
    }

    [AvaloniaTest]
    public async Task OffscreenSelectedClipsReleaseControlsAndRestoreDeselectionOnScrollBack()
    {
        using var host = await TimelineTestHost.CreateAsync("deselect-offscreen", MediaTime.FromSeconds(120),
            [new(MediaTime.Zero, MediaTime.FromSeconds(2), 0, new ElementSource.EngineObject(() => new Beutl.Graphics.Shapes.RectShape()))]);
        ElementViewModel clip = host.Model.Elements.Single();
        host.Model.SelectElement(clip);
        HeadlessTestHelpers.Render();
        Assert.That(FindView(host, clip), Is.Not.Null);
        host.Content.Offset = new Vector(MediaTime.FromSeconds(40).TimeToPixel(1), 0);
        HeadlessTestHelpers.Render();
        await HeadlessTestHelpers.WaitUntilAsync(() => FindView(host, clip) == null);
        Assert.That(clip.IsSelected.Value, Is.True, "Recycling preserves the selection in the model.");
        Assert.That(clip.GetMissingThumbnailIndices, Is.Null);
        host.Model.ClearSelected();
        HeadlessTestHelpers.Render();
        Assert.That(FindView(host, clip), Is.Null);
        Assert.That(clip.IsSelected.Value, Is.False);
        Assert.That(clip.GetMissingThumbnailIndices, Is.Null);

        host.Content.Offset = Vector.Zero;
        await HeadlessTestHelpers.WaitUntilAsync(() => FindView(host, clip) != null);
        Assert.That(clip.IsSelected.Value, Is.False);
        Assert.That(clip.GetMissingThumbnailIndices, Is.Not.Null);
    }

    [AvaloniaTest]
    public async Task DistantEditFlashesAfterScrollingRealizesItsControl()
    {
        using var host = await TimelineTestHost.CreateAsync("highlight-offscreen", MediaTime.FromSeconds(120),
            [new(MediaTime.FromSeconds(60), MediaTime.FromSeconds(2), 0, new ElementSource.EngineObject(() => new Beutl.Graphics.Shapes.RectShape()))]);
        ElementViewModel clip = host.Model.Elements.Single();
        host.Model.ClearSelected();
        HeadlessTestHelpers.Render();
        Assert.That(FindView(host, clip), Is.Null);
        host.Model.ScrollTo.Execute((clip.Model.Range, 0));
        host.Model.HighlightElement.Execute(clip.Model);
        await HeadlessTestHelpers.WaitUntilAsync(() => FindView(host, clip) is { } view
            && AdornerLayer.GetAdornerLayer(view.border)?.Children.Any(c => AdornerLayer.GetAdornedElement(c) == view.border) == true);
        Assert.That(host.Content.Offset.X, Is.EqualTo(MediaTime.FromSeconds(60).TimeToPixel(1) - 40).Within(0.1));
        await Task.Delay(250);
        HeadlessTestHelpers.Render();
        HeadlessCapture.SaveIfRequested(host.View, nameof(TimelineVirtualizationTests), "distant-edit-highlight");
    }

    private static ElementView? FindView(TimelineTestHost host, ElementViewModel model)
        => host.View.GetVisualDescendants().OfType<ElementView>().SingleOrDefault(view => view.DataContext == model);
}
