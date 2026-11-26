using System.Collections.Immutable;
using System.Diagnostics;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
        Assert.That(host.Views.Single().DataContext, Is.SameAs(host.Model.GetViewModelFor(scene.Children.Single())));
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
            // The template flyout shows a picker presenter, and the color picker the presenter of Beutl's picker.
            Popup popup = host.Window.OpenedPopups.Single(open => open.Child
                is FluentAvalonia.UI.Controls.FAPickerFlyoutPresenter
                or Beutl.Controls.PropertyEditors.SimpleColorPickerFlyoutPresenter);
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
        Assert.That(host.Model.RealizedElements, Is.Empty, "Clips this narrow are painted from their elements.");
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
    [TestCase(2, 10_000)]
    [TestCase(200, 100)]
    public void Twenty_thousand_clips_only_create_view_models_for_those_shown_or_selected(int rows, int columns)
    {
        var scene = new Scene { Duration = { Value = MediaTime.FromSeconds(columns * 2 + 10) } };
        scene.Children.AddRange(Enumerable.Range(0, rows * columns).Select(i => Clip(i / columns, i % columns * 2)));
        using var host = new ViewportHost(scene);
        Assert.That(host.Model.RealizedElements, Has.Count.InRange(1, 250));
        Element distant = scene.Children[^1];
        Assert.That(host.Model.FindViewModelFor(distant), Is.Null);

        host.Model.Options.Value = host.Model.Options.Value with { Scale = 0.5f };
        HeadlessTestHelpers.Render(3);
        Assert.That(host.Model.RealizedElements, Has.Count.InRange(1, 500), "Zooming only adds the clips it brings into view.");

        host.Selection.SelectedElements.Add(distant);
        Assert.That(host.Model.FindViewModelFor(distant)!.IsSelected.Value, Is.True);
        Assert.That(host.Model.SelectedElements.Select(item => item.Model), Is.EqualTo(new[] { distant }));
        Assert.That(host.Views.Select(view => view.DataContext), Does.Not.Contain(host.Model.FindViewModelFor(distant)));

        host.Selection.SelectedElements.Clear();
        Assert.That(host.Model.SelectedElements, Is.Empty);
        Assert.That(host.Model.RealizedElements, Has.Count.InRange(1, 501));
    }

    [AvaloniaTest]
    public void Narrow_clips_paint_the_same_without_a_view_model()
    {
        var scene = new Scene { Duration = { Value = MediaTime.FromSeconds(100) } };
        Element plain = Clip(0, 0);
        Element colored = Clip(1, 5);
        colored.AccentColor.Value = Beutl.Media.Colors.Red;
        Element disabled = Clip(2, 10);
        disabled.IsEnabled.Value = false;
        Element locked = Clip(3, 15);
        locked.IsLocked.Value = true;
        Element thin = Clip(4, 20);
        thin.Length.Value = MediaTime.FromSeconds(0.1);
        Element[] clips = [plain, colored, disabled, locked, thin];
        scene.Children.AddRange(clips);
        using var host = new ViewportHost(scene, scale: 0.1f);
        Assert.That(host.Model.RealizedElements, Is.Empty);
        byte[] painted = CapturePixels(host.Window);

        foreach (Element clip in clips)
        {
            host.Model.GetViewModelFor(clip);
        }

        HeadlessTestHelpers.Render(3);

        Assert.That(host.Model.RealizedElements, Has.Count.EqualTo(clips.Length));
        Assert.That(host.Views, Is.Empty);
        Assert.That(CapturePixels(host.Window), Is.EqualTo(painted));
    }

    [AvaloniaTest]
    public void A_narrow_clip_gets_its_view_model_when_the_pointer_reaches_it()
    {
        var scene = new Scene { Duration = { Value = MediaTime.FromSeconds(100) } };
        Element reached = Clip(0, 0);
        Element other = Clip(0, 5);
        scene.Children.AddRange([reached, other]);
        using var host = new ViewportHost(scene, scale: 0.1f);
        Assert.That(host.Model.RealizedElements, Is.Empty);

        host.Window.MouseMove(host.Panel.TranslatePoint(new Point(5, 12.5), host.Window)!.Value);
        HeadlessTestHelpers.Render(3);

        Assert.That(host.Model.RealizedElements.Select(item => item.Model), Is.EqualTo(new[] { reached }));
        Assert.That(host.Views.Select(view => ((ElementViewModel)view.DataContext!).Model), Is.EqualTo(new[] { reached }));
    }

    [AvaloniaTest]
    public void A_narrow_clip_without_a_view_model_follows_its_element()
    {
        var scene = new Scene { Duration = { Value = MediaTime.FromSeconds(100) } };
        Element clip = Clip(0, 0);
        scene.Children.Add(clip);
        using var host = new ViewportHost(scene, scale: 0.1f);
        Rect bounds = host.Model.GetElementBounds(clip);

        clip.Start.Value = MediaTime.FromSeconds(10);
        clip.ZIndex.Value = 2;
        HeadlessTestHelpers.Render(3);
        Rect moved = host.Model.GetElementBounds(clip);
        Assert.That(moved, Is.Not.EqualTo(bounds));
        Assert.That(host.Model.FindViewModelFor(clip), Is.Null);

        host.Window.MouseMove(host.Panel.TranslatePoint(moved.Center, host.Window)!.Value);
        HeadlessTestHelpers.Render(3);
        Assert.That(host.Views.Select(view => ((ElementViewModel)view.DataContext!).Model), Is.EqualTo(new[] { clip }));
    }

    private static byte[] CapturePixels(Window window)
    {
        HeadlessTestHelpers.Render(3);
        using WriteableBitmap frame = window.CaptureRenderedFrame()!;
        using ILockedFramebuffer buffer = frame.Lock();
        var pixels = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
        return pixels;
    }

    [AvaloniaTest]
    public void An_edit_brings_a_clip_without_a_view_model_into_view_and_on_its_row()
    {
        var scene = new Scene { Duration = { Value = MediaTime.FromSeconds(100) } };
        Element near = Clip(0, 0);
        Element distant = Clip(30, 50);
        scene.Children.AddRange([near, distant]);
        using var host = new ViewportHost(scene);
        Assert.That(host.Model.FindViewModelFor(distant), Is.Null);

        // The row exists at once, as a clip with a view model would have asked for it.
        distant.ZIndex.Value = 60;
        Assert.That(host.Model.LayerHeaders, Has.Count.EqualTo(61));
        Assert.That(host.Model.FindViewModelFor(distant), Is.Null);

        // A clip keeps its row, whether it has a view model or not.
        host.Model.Options.Value = host.Model.Options.Value with { MaxLayerCount = 1 };
        Assert.That(host.Model.LayerHeaders, Has.Count.EqualTo(61));

        distant.Start.Value = MediaTime.Zero;
        distant.ZIndex.Value = 1;
        HeadlessTestHelpers.Render(3);
        Assert.That(host.Views.Select(view => ((ElementViewModel)view.DataContext!).Model), Is.EquivalentTo(new[] { near, distant }));

        // The rows left empty go when the count asks for it.
        host.Model.Options.Value = host.Model.Options.Value with { MaxLayerCount = 1 };
        Assert.That(host.Model.LayerHeaders, Has.Count.EqualTo(2));

        scene.Children.Remove(distant);
        HeadlessTestHelpers.Render(3);
        Assert.That(host.Model.GetViewModelFor(distant), Is.Null);
        Assert.That(host.Model.RealizedElements.Select(item => item.Model), Is.EqualTo(new[] { near }));
        Assert.That(host.Views.Select(view => ((ElementViewModel)view.DataContext!).Model), Is.EqualTo(new[] { near }));
    }

    [AvaloniaTest]
    public void Clips_stay_in_scene_order_whichever_got_its_view_model_first()
    {
        var scene = new Scene { Duration = { Value = MediaTime.FromSeconds(100) } };
        Element first = Clip(0, 50);
        Element second = Clip(0, 50);
        scene.Children.AddRange([first, second]);
        using var host = new ViewportHost(scene);

        // The later clip gets its view model first, and the view still paints the clips in scene order.
        host.Selection.SelectedElements.Add(second);
        host.Selection.SelectedElements.Clear();
        Assert.That(host.Model.FindViewModelFor(first), Is.Null);
        host.Scroll(MediaTime.FromSeconds(50).TimeToPixel(1), 0);
        Assert.That(host.Views.Select(view => ((ElementViewModel)view.DataContext!).Model), Is.EqualTo(new[] { first, second }));
    }

    [AvaloniaTest]
    public void A_portal_added_to_a_clip_without_a_view_model_shows_its_scope()
    {
        var scene = new Scene { Duration = { Value = MediaTime.FromSeconds(100) } };
        scene.Children.Add(Clip(30, 0));
        using var host = new ViewportHost(scene);
        host.Scroll(0, host.Model.CalculateLayerTop(25));
        Element added = Clip(1, 0);
        scene.Children.Add(added);
        HeadlessTestHelpers.Render();
        Assert.That(host.Model.FindViewModelFor(added), Is.Null);

        added.Objects.Add(new PortalObject { Count = { Value = 35 } });
        HeadlessTestHelpers.Render(3);
        Assert.That(host.Panel.Children.OfType<ElementScopeView>().Single().DataContext,
            Is.SameAs(host.Model.FindViewModelFor(added)!.Scope));
    }

    [AvaloniaTest]
    public async Task The_razor_cuts_a_clip_without_a_view_model_and_leaves_a_locked_one()
    {
        using var host = await TimelineTestHost.CreateAsync("razor-no-view-model", MediaTime.FromSeconds(120));
        Element distant = Clip(0, 100);
        Element locked = Clip(1, 100);
        locked.IsLocked.Value = true;
        Element elsewhere = Clip(2, 110);
        host.Scene.Children.AddRange([distant, locked, elsewhere]);
        HeadlessTestHelpers.Render(3);
        Assert.That(host.Model.FindViewModelFor(distant), Is.Null);

        host.Model.RazorSplitAt(MediaTime.FromSeconds(100.5));

        Assert.Multiple(() =>
        {
            Assert.That(distant.Length.Value, Is.EqualTo(MediaTime.FromSeconds(0.5)));
            Assert.That(locked.Length.Value, Is.EqualTo(MediaTime.FromSeconds(1.5)));
            Assert.That(elsewhere.Length.Value, Is.EqualTo(MediaTime.FromSeconds(1.5)));
            Assert.That(host.Scene.Children, Has.Count.EqualTo(4));
        });
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
        ElementViewModel model = host.Model.AllElements()[1];
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
        float scale = host.Model.Options.Value.Scale;
        foreach (Element element in host.Model.Scene.Children)
        {
            var bounds = new Rect(element.Start.Value.TimeToPixel(scale), host.Model.CalculateLayerTop(element.ZIndex.Value),
                element.Length.Value.TimeToPixel(scale), FrameNumberHelper.LayerHeight);
            if (bounds.Intersects(viewport))
                Assert.That(visible.Contains(host.Model.FindViewModelFor(element)), Is.True, element.Name.Value);
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

        PreviewSourceMode originalMode = TestShell.Configuration.EditorConfig.PreviewSourceMode.Value;
        int initialRequests = firstSource.Requests;
        try
        {
            TestShell.Configuration.EditorConfig.PreviewSourceMode.Value = originalMode == PreviewSourceMode.PreferProxy
                ? PreviewSourceMode.ForceOriginal : PreviewSourceMode.PreferProxy;
            HeadlessTestHelpers.Render();
            Assert.That(firstSource.Requests, Is.GreaterThan(initialRequests));
            Assert.That(distantSource.Requests, Is.Zero);
        }
        finally
        {
            TestShell.Configuration.EditorConfig.PreviewSourceMode.Value = originalMode;
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
    public async Task Thumbnails_off_by_default_load_only_for_clips_turned_on_and_survive_a_reload()
    {
        var scene = new Scene { Duration = { Value = MediaTime.FromSeconds(100) } };
        Element chosen = Clip(0, 0);
        Element other = Clip(1, 0);
        var chosenSource = new CountingThumbnailSource();
        var otherSource = new CountingThumbnailSource();
        chosen.Objects.Add(chosenSource);
        other.Objects.Add(otherSource);
        scene.Children.AddRange([chosen, other]);

        IProperty<bool> showByDefault = TestShell.Configuration.EditorConfig.ShowTimelineThumbnailsByDefault;
        bool original = showByDefault.Value;
        showByDefault.Value = false;
        try
        {
            var state = new System.Text.Json.Nodes.JsonObject();
            using (var host = new ViewportHost(scene))
            {
                ElementViewModel chosenModel = host.Model.GetViewModelFor(chosen)!;
                ElementViewModel otherModel = host.Model.GetViewModelFor(other)!;
                await Task.Delay(550);
                HeadlessTestHelpers.Render();
                Assert.Multiple(() =>
                {
                    Assert.That(chosenModel.IsThumbnailsDisabled.Value, Is.True);
                    Assert.That(otherModel.IsThumbnailsDisabled.Value, Is.True);
                    Assert.That(chosenSource.Requests + otherSource.Requests, Is.Zero, "No clip decodes while the default is off.");
                });

                chosenModel.IsThumbnailsDisabled.Value = false;
                await HeadlessTestHelpers.WaitUntilAsync(() => chosenSource.Requests > 0);
                Assert.That(host.Model.ThumbnailsEnabledElements, Is.EqualTo(new[] { chosen.Id.Value }));
                Assert.That(host.Model.ThumbnailsDisabledElements, Is.Empty);

                showByDefault.Value = true;
                await HeadlessTestHelpers.WaitUntilAsync(() => otherSource.Requests > 0);
                Assert.That(chosenModel.IsThumbnailsDisabled.Value, Is.False);
                Assert.That(otherModel.IsThumbnailsDisabled.Value, Is.False, "A clip without its own choice follows the default.");

                showByDefault.Value = false;
                HeadlessTestHelpers.Settle();
                Assert.That(chosenModel.IsThumbnailsDisabled.Value, Is.False, "The clip turned on keeps its choice.");
                Assert.That(otherModel.IsThumbnailsDisabled.Value, Is.True);

                // A choice made under one default replaces the one made under the other.
                showByDefault.Value = true;
                HeadlessTestHelpers.Settle();
                otherModel.IsThumbnailsDisabled.Value = true;
                showByDefault.Value = false;
                HeadlessTestHelpers.Settle();
                otherModel.IsThumbnailsDisabled.Value = false;
                showByDefault.Value = true;
                HeadlessTestHelpers.Settle();
                Assert.That(otherModel.IsThumbnailsDisabled.Value, Is.False, "The newer choice wins after the default flips back.");
                Assert.That(host.Model.ThumbnailsDisabledElements, Does.Not.Contain(other.Id.Value));
                otherModel.IsThumbnailsDisabled.Value = true;
                showByDefault.Value = false;
                HeadlessTestHelpers.Settle();
                Assert.That(chosenModel.IsThumbnailsDisabled.Value, Is.False, "Flipping the default keeps saved choices.");
                Assert.That(otherModel.IsThumbnailsDisabled.Value, Is.True);
                host.Model.WriteToJson(state);
            }

            using (var reopened = new ViewportHost(scene))
            {
                reopened.Model.ReadFromJson(state);
                Assert.That(reopened.Model.GetViewModelFor(chosen)!.IsThumbnailsDisabled.Value, Is.False);
                Assert.That(reopened.Model.GetViewModelFor(other)!.IsThumbnailsDisabled.Value, Is.True);
            }
        }
        finally
        {
            showByDefault.Value = original;
            HeadlessTestHelpers.Render();
        }
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

    [AvaloniaTest]
    public async Task Layer_height_changes_regenerate_the_video_strip_without_reprocessing_audio()
    {
        await TestReset.ResetShellAsync();
        EditorConfig config = TestShell.Configuration.EditorConfig;
        TimelineLayerSize previous = config.TimelineLayerSize.Value;
        bool showByDefault = config.ShowTimelineThumbnailsByDefault.Value;
        try
        {
            config.TimelineLayerSize.Value = TimelineLayerSize.Small;
            config.ShowTimelineThumbnailsByDefault.Value = true;
            var scene = new Scene { Duration = { Value = MediaTime.FromSeconds(10) } };
            Element video = Clip(0, 0);
            video.Length.Value = MediaTime.FromSeconds(3.5);
            video.Name.Value = "Video Clip";
            Element audio = Clip(1, 0);
            audio.Length.Value = MediaTime.FromSeconds(3.5);
            audio.Name.Value = "Audio Clip";
            var videoSource = new CountingThumbnailSource(ThumbnailsKind.Video, scaledSlots: true);
            var audioSource = new CountingThumbnailSource();
            video.Objects.Add(videoSource);
            audio.Objects.Add(audioSource);
            scene.Children.AddRange([video, audio]);
            using var host = new ViewportHost(scene);
            host.Window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            HeadlessTestHelpers.Render(3);
            await HeadlessTestHelpers.WaitUntilAsync(() =>
                videoSource.LastRequestedHeight == 25
                && host.Model.GetViewModelFor(video)!.VideoThumbnailCount.Value > 0
                && videoSource.YieldedAtHeight(25, host.Model.GetViewModelFor(video)!.VideoThumbnailCount.Value - 1)
                && audioSource.Requests > 0);
            int audioRequests = audioSource.Requests;
            HeadlessTestHelpers.Render(3);
            HeadlessCapture.SaveIfRequested(host.Window, nameof(TimelineVirtualizationTests), "media-rows-small");

            config.TimelineLayerSize.Value = TimelineLayerSize.Medium;
            config.TimelineLayerSize.Value = TimelineLayerSize.Large;
            await HeadlessTestHelpers.WaitUntilAsync(() =>
                videoSource.LastRequestedHeight == 64
                && host.Model.GetViewModelFor(video)!.VideoThumbnailCount.Value > 0
                && videoSource.YieldedAtHeight(64, host.Model.GetViewModelFor(video)!.VideoThumbnailCount.Value - 1));
            HeadlessTestHelpers.Render(3);
            Assert.That(audioSource.Requests, Is.EqualTo(audioRequests));
            HeadlessCapture.SaveIfRequested(host.Window, nameof(TimelineVirtualizationTests), "media-rows-large");

            config.TimelineLayerSize.Value = TimelineLayerSize.Medium;
            await HeadlessTestHelpers.WaitUntilAsync(() =>
                videoSource.LastRequestedHeight == 40
                && host.Model.GetViewModelFor(video)!.VideoThumbnailCount.Value > 0
                && videoSource.YieldedAtHeight(40, host.Model.GetViewModelFor(video)!.VideoThumbnailCount.Value - 1));
            HeadlessTestHelpers.Render(3);
            Assert.That(audioSource.Requests, Is.EqualTo(audioRequests));
            HeadlessCapture.SaveIfRequested(host.Window, nameof(TimelineVirtualizationTests), "media-rows-medium");
        }
        finally
        {
            config.TimelineLayerSize.Value = previous;
            config.ShowTimelineThumbnailsByDefault.Value = showByDefault;
        }
    }

    [AvaloniaTest]
    public async Task Enlarging_layer_height_on_a_scrolled_video_clip_rebuilds_the_visible_slots()
    {
        await TestReset.ResetShellAsync();
        EditorConfig config = TestShell.Configuration.EditorConfig;
        TimelineLayerSize previous = config.TimelineLayerSize.Value;
        bool showByDefault = config.ShowTimelineThumbnailsByDefault.Value;
        try
        {
            config.TimelineLayerSize.Value = TimelineLayerSize.Small;
            config.ShowTimelineThumbnailsByDefault.Value = true;
            var scene = new Scene { Duration = { Value = MediaTime.FromSeconds(20) } };
            var video = new Element
            {
                ZIndex = { Value = 0 },
                Start = { Value = MediaTime.Zero },
                Length = { Value = MediaTime.FromSeconds(20) },
                Name = { Value = "Long Video" }
            };
            var videoSource = new CountingThumbnailSource(ThumbnailsKind.Video, scaledSlots: true);
            video.Objects.Add(videoSource);
            scene.Children.Add(video);
            using var host = new ViewportHost(scene);
            ElementViewModel model = host.Model.GetViewModelFor(video)!;

            ThumbnailStripControl strip = host.Panel.GetVisualDescendants().OfType<ThumbnailStripControl>().Single();
            await HeadlessTestHelpers.WaitUntilAsync(() =>
                model.VideoThumbnailCount.Value > 40
                && strip.GetMissingIndices(0, model.VideoThumbnailCount.Value - 1).Count == 0);
            int smallCount = model.VideoThumbnailCount.Value;

            // Scroll near the right end so ElementViewModel caches a Small slot range past Large's total slot count.
            int scrolledStart = -1;
            strip.VisibleRangeChanged += OnVisibleRangeChanged;
            try
            {
                // ThumbnailStripControl computes the visible range on OffsetProperty changes before layout settles,
                // so a second scroll step after Render observes the scrolled layout bounds.
                host.Scroll(2000, 0);
                host.Scroll(2010, 0);
                await HeadlessTestHelpers.WaitUntilAsync(() => scrolledStart > 25);
            }
            finally
            {
                strip.VisibleRangeChanged -= OnVisibleRangeChanged;
            }

            // Keep a Small-height scroll request in flight so it delivers a stale tile during the 500 ms debounce.
            var releaseStaleTile = new TaskCompletionSource();
            videoSource.PauseAfterFirstTileAtHeight = (25, releaseStaleTile);
            strip.ClearThumbnails();
            model.OnVisibleRangeChanged(smallCount - 3, smallCount - 1);
            await HeadlessTestHelpers.WaitUntilAsync(() => videoSource.PausedAtHeight == 25);

            config.TimelineLayerSize.Value = TimelineLayerSize.Large;
            // Release the in-flight Small tile and emit an old-count visible range while the rebuild is pending.
            releaseStaleTile.TrySetResult();
            model.OnVisibleRangeChanged(smallCount - 1, smallCount - 1);
            await HeadlessTestHelpers.WaitUntilAsync(() =>
                videoSource.LastRequestedHeight == 64 && model.VideoThumbnailCount.Value < smallCount);
            HeadlessTestHelpers.Render(3);
            int largeCount = model.VideoThumbnailCount.Value;

            await HeadlessTestHelpers.WaitUntilAsync(() =>
                videoSource.YieldedAtHeight(64, largeCount - 1)
                && strip.GetMissingIndices(Math.Max(0, largeCount - 3), largeCount - 1).Count == 0);
            Assert.That(largeCount, Is.LessThan(smallCount));

            void OnVisibleRangeChanged(int start, int _) => scrolledStart = start;
        }
        finally
        {
            config.TimelineLayerSize.Value = previous;
            config.ShowTimelineThumbnailsByDefault.Value = showByDefault;
        }
    }

    [AvaloniaTest]
    public async Task Failed_video_height_rebuild_recovers_on_subsequent_visible_range_without_reusing_stale_indices()
    {
        await TestReset.ResetShellAsync();
        EditorConfig config = TestShell.Configuration.EditorConfig;
        TimelineLayerSize previous = config.TimelineLayerSize.Value;
        bool showByDefault = config.ShowTimelineThumbnailsByDefault.Value;
        try
        {
            config.TimelineLayerSize.Value = TimelineLayerSize.Small;
            config.ShowTimelineThumbnailsByDefault.Value = true;
            var scene = new Scene { Duration = { Value = MediaTime.FromSeconds(20) } };
            var video = new Element
            {
                ZIndex = { Value = 0 },
                Start = { Value = MediaTime.Zero },
                Length = { Value = MediaTime.FromSeconds(20) },
                Name = { Value = "Long Video" }
            };
            var videoSource = new CountingThumbnailSource(ThumbnailsKind.Video, scaledSlots: true);
            video.Objects.Add(videoSource);
            scene.Children.Add(video);
            using var host = new ViewportHost(scene);
            ElementViewModel model = host.Model.GetViewModelFor(video)!;

            ThumbnailStripControl strip = host.Panel.GetVisualDescendants().OfType<ThumbnailStripControl>().Single();
            await HeadlessTestHelpers.WaitUntilAsync(() =>
                model.VideoThumbnailCount.Value > 40
                && strip.GetMissingIndices(0, model.VideoThumbnailCount.Value - 1).Count == 0);
            int smallCount = model.VideoThumbnailCount.Value;

            int requestsBeforeResize = videoSource.Requests;
            videoSource.FailNextVideoStrip = true;
            config.TimelineLayerSize.Value = TimelineLayerSize.Large;

            await HeadlessTestHelpers.WaitUntilAsync(() =>
                videoSource.Requests > requestsBeforeResize
                && !videoSource.FailNextVideoStrip
                && videoSource.LastRequestedHeight == 64);
            HeadlessTestHelpers.Settle();
            Assert.That(model.VideoThumbnailCount.Value, Is.EqualTo(smallCount));

            // A subsequent visible-range request with stale Small indices must trigger a full Large rebuild.
            model.OnVisibleRangeChanged(smallCount - 3, smallCount - 1);
            await HeadlessTestHelpers.WaitUntilAsync(() =>
                model.VideoThumbnailCount.Value < smallCount
                && videoSource.YieldedAtHeight(64, model.VideoThumbnailCount.Value - 1));
            HeadlessTestHelpers.Render(3);
            int largeCount = model.VideoThumbnailCount.Value;
            Assert.Multiple(() =>
            {
                Assert.That(largeCount, Is.LessThan(smallCount));
                Assert.That(videoSource.LastRequestedHeight, Is.EqualTo(64));
                Assert.That(videoSource.LastRequestedStart, Is.Zero);
                Assert.That(videoSource.LastRequestedEnd, Is.EqualTo(-1));
                Assert.That(strip.GetMissingIndices(0, largeCount - 1), Is.Empty);
            });
        }
        finally
        {
            config.TimelineLayerSize.Value = previous;
            config.ShowTimelineThumbnailsByDefault.Value = showByDefault;
        }
    }

    [AvaloniaTest]
    public async Task A_visible_range_only_requests_the_missing_thumbnail_slots()
    {
        await TestReset.ResetShellAsync();
        EditorConfig config = TestShell.Configuration.EditorConfig;
        bool showByDefault = config.ShowTimelineThumbnailsByDefault.Value;
        try
        {
            config.ShowTimelineThumbnailsByDefault.Value = true;
            var scene = new Scene { Duration = { Value = MediaTime.FromSeconds(8) } };
            var element = new Element { Length = { Value = MediaTime.FromSeconds(8) } };
            var source = new CountingThumbnailSource(ThumbnailsKind.Video, scaledSlots: true);
            element.Objects.Add(source);
            scene.Children.Add(element);
            using var host = new ViewportHost(scene);
            var model = host.Model.GetViewModelFor(element)!;
            var strip = host.Panel.GetVisualDescendants().OfType<ThumbnailStripControl>().Single();
            await HeadlessTestHelpers.WaitUntilAsync(() =>
                model.VideoThumbnailCount.Value >= 5
                && strip.GetMissingIndices(0, model.VideoThumbnailCount.Value - 1).Count == 0);

            strip.ClearThumbnails();
            for (int i = 0; i < model.VideoThumbnailCount.Value; i++)
            {
                if (i != 1 && i != 3)
                    strip.SetThumbnail(i, null);
            }
            int refresh = source.RequestedVideoRanges.Count;
            model.OnVisibleRangeChanged(0, 4);
            await HeadlessTestHelpers.WaitUntilAsync(() => strip.GetMissingIndices(0, 4).Count == 0);

            Assert.That(source.RequestedVideoRanges.Skip(refresh), Is.EqualTo(new[] { (1, 1), (3, 3) }));
        }
        finally
        {
            config.ShowTimelineThumbnailsByDefault.Value = showByDefault;
        }
    }

    [SuppressResourceClassGeneration]
    private sealed class CountingThumbnailSource(ThumbnailsKind kind = ThumbnailsKind.Audio, bool scaledSlots = false) : EngineObject, IThumbnailsProvider
    {
        private readonly HashSet<(int Height, int Index)> _yielded = [];
        public int Requests { get; private set; }
        public int LastRequestedHeight { get; private set; }
        public int LastRequestedStart { get; private set; }
        public int LastRequestedEnd { get; private set; }
        public List<(int Start, int End)> RequestedVideoRanges { get; } = [];
        public bool FailNextVideoStrip { get; set; }
        public (int Height, TaskCompletionSource Gate)? PauseAfterFirstTileAtHeight { get; set; }
        public int PausedAtHeight { get; private set; }
        public ThumbnailsKind ThumbnailsKind => kind;
        public bool HoldRequests { get; set; }
        public CancellationToken LastRequestCancellation { get; private set; }
        public event EventHandler? ThumbnailsInvalidated;
        public void Invalidate() => ThumbnailsInvalidated?.Invoke(this, EventArgs.Empty);
        public bool YieldedAtHeight(int height, int index) => _yielded.Contains((height, index));

        private Task BeginRequest(CancellationToken cancellationToken)
        {
            Requests++;
            LastRequestCancellation = cancellationToken;
            return HoldRequests ? Task.Delay(Timeout.Infinite, cancellationToken) : Task.CompletedTask;
        }

        public async IAsyncEnumerable<WaveformChunk> GetWaveformChunksAsync(int chunkCount, int samplesPerChunk,
            IThumbnailCacheService? cacheService,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default, Beutl.Media.Decoding.DecoderRegistry? decoders = null)
        {
            await BeginRequest(cancellationToken);
            for (int i = 0; i < chunkCount; i++)
            {
                double t = chunkCount > 1 ? (double)i / (chunkCount - 1) : 0;
                double envelope = 0.25 + 0.65 * Math.Abs(Math.Sin(t * Math.PI * 5.0));
                float amp = (float)(envelope * (0.4 + 0.6 * Math.Abs(Math.Sin(i * 0.45))));
                yield return new WaveformChunk(i, -amp, amp);
            }
        }

        public async IAsyncEnumerable<(int Index, int Count, Beutl.Media.Bitmap Thumbnail)> GetThumbnailStripAsync(
            Beutl.Media.FontManager fonts,
            int maxWidth, int maxHeight, IThumbnailCacheService? cacheService = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default,
            int startIndex = 0, int endIndex = -1, Beutl.Media.Decoding.DecoderRegistry? decoders = null)
        {
            LastRequestedHeight = maxHeight;
            LastRequestedStart = startIndex;
            LastRequestedEnd = endIndex;
            RequestedVideoRanges.Add((startIndex, endIndex));
            await BeginRequest(cancellationToken);
            if (FailNextVideoStrip)
            {
                FailNextVideoStrip = false;
                throw new InvalidOperationException("Simulated thumbnail decode failure.");
            }
            if (scaledSlots)
            {
                int count = Beutl.Graphics.SourceVideo.GetVideoThumbnailCount(new Beutl.Media.PixelSize(160, 90), maxWidth, maxHeight);
                int effectiveStart = Math.Max(0, startIndex);
                int effectiveEnd = endIndex < 0 ? count - 1 : Math.Min(endIndex, count - 1);
                int tileWidth = Math.Max(4, (int)Math.Round(maxHeight * 16.0 / 9.0));
                int tileHeight = Math.Max(4, maxHeight);
                await Task.Yield();
                for (int i = effectiveStart; i <= effectiveEnd; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (i == effectiveStart + 1 && PauseAfterFirstTileAtHeight is { } pause && pause.Height == maxHeight)
                    {
                        PauseAfterFirstTileAtHeight = null;
                        PausedAtHeight = maxHeight;
                        await pause.Gate.Task.WaitAsync(cancellationToken);
                    }

                    _yielded.Add((maxHeight, i));
                    yield return (i, count, CreateFrameTile(tileWidth, tileHeight, i, count));
                }
                yield break;
            }

            var bitmap = new Beutl.Media.Bitmap(4, 4);
            bitmap.GetPixelSpan<Beutl.Media.Pixel.Bgra8888>().Fill(new Beutl.Media.Pixel.Bgra8888(180, 100, 40, 255));
            yield return (0, 1, bitmap);
        }

        private static Beutl.Media.Bitmap CreateFrameTile(int width, int height, int index, int count)
        {
            var bitmap = new Beutl.Media.Bitmap(width, height);
            Span<Beutl.Media.Pixel.Bgra8888> pixels = bitmap.GetPixelSpan<Beutl.Media.Pixel.Bgra8888>();
            double progress = count > 1 ? (double)index / (count - 1) : 0;
            double sunX = width * (0.2 + 0.6 * progress);
            double sunY = height * (0.3 + 0.15 * Math.Sin(progress * Math.PI));
            double sunRadius = Math.Max(2.5, height * 0.18);

            for (int y = 0; y < height; y++)
            {
                double ny = (double)y / height;
                for (int x = 0; x < width; x++)
                {
                    byte r, g, b;
                    if (x == 0)
                    {
                        r = 20; g = 24; b = 30;
                    }
                    else
                    {
                        double ridge = 0.62 + 0.14 * Math.Sin((x / (double)width + progress) * Math.PI * 2.0);
                        double dx = x - sunX;
                        double dy = y - sunY;
                        if (dx * dx + dy * dy <= sunRadius * sunRadius)
                        {
                            r = 250; g = 210; b = 95;
                        }
                        else if (ny >= ridge)
                        {
                            r = (byte)(36 + 24 * progress);
                            g = (byte)(78 - 18 * ny);
                            b = (byte)(58 + 20 * (1 - ny));
                        }
                        else
                        {
                            r = (byte)(40 + 135 * progress * (1 - ny * 0.5));
                            g = (byte)(85 + 75 * (1 - ny));
                            b = (byte)(155 - 45 * progress + 40 * (1 - ny));
                        }
                    }

                    pixels[y * width + x] = new Beutl.Media.Pixel.Bgra8888(b, g, r, 255);
                }
            }

            return bitmap;
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
            var context = TestShell.CreateEditorContextMock();
            context.Setup(x => x.GetService(typeof(Beutl.Configuration.GlobalConfiguration))).Returns(TestShell.Configuration);
            context.Setup(x => x.GetService(typeof(Beutl.Media.FontManager))).Returns(TestShell.FontManager);
            context.Setup(x => x.GetService(typeof(Beutl.Media.Decoding.DecoderRegistry))).Returns(TestShell.Decoders);
            context.Setup(x => x.GetService(typeof(Beutl.Services.INotificationService))).Returns(TestShell.Notifications);
            context.Setup(x => x.GetService(typeof(ITimelineOptionsProvider))).Returns(_options);
            context.Setup(x => x.GetService(typeof(IEditorClock))).Returns(_clock);
            context.Setup(x => x.GetService(typeof(IEditorSelection))).Returns(Selection);
            context.Setup(x => x.GetService(typeof(IBufferStatus))).Returns(buffer.Object);
            context.Setup(x => x.GetService(typeof(Beutl.Services.LibraryService))).Returns(TestShell.Library);
            context.Setup(x => x.GetService(typeof(Beutl.Editor.Services.ObjectTemplateService))).Returns(TestShell.Templates);
            context.Setup(x => x.GetService(typeof(Beutl.Editor.Components.FileBrowserTab.Services.FileThumbnailService))).Returns(TestShell.Thumbnails);
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
            int count = Model.Scene.Children.Count;
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
        ElementViewModel clip = host.Model.AllElements().Single();
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
        ElementViewModel clip = host.Model.AllElements().Single();
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
