using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using Vector2 = System.Numerics.Vector2;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class TimelineFitTests
{
    // TimelineTabView's FitMargin.
    private const double Margin = 32;

    [AvaloniaTest]
    public void A_range_fills_the_width_less_a_margin_at_each_side()
    {
        (float scale, double offset) = TimelineHelper.FitRange(Range(10, 20), 1000, Margin);
        Assert.Multiple(() =>
        {
            Assert.That(InView(10, scale, offset), Is.EqualTo(Margin).Within(0.01));
            Assert.That(InView(20, scale, offset), Is.EqualTo(1000 - Margin).Within(0.01));
        });
    }

    // The view cannot scroll left of zero, so the margin goes to the right.
    [AvaloniaTest]
    [TestCase(0d)]
    [TestCase(0.1d)]
    public void A_range_that_starts_near_zero_starts_at_the_left_edge(double start)
    {
        (float scale, double offset) = TimelineHelper.FitRange(Range(start, 30), 1000, Margin);
        Assert.Multiple(() =>
        {
            Assert.That(offset, Is.Zero);
            Assert.That(InView(30, scale, offset), Is.EqualTo(1000 - Margin).Within(0.01));
        });
    }

    [AvaloniaTest]
    [TestCase(5d, 5.5d)]
    [TestCase(5d, 5d)]
    public void A_range_too_short_for_the_largest_scale_is_centered(double start, double end)
    {
        (float scale, double offset) = TimelineHelper.FitRange(Range(start, end), 1000, Margin);
        Assert.Multiple(() =>
        {
            Assert.That(scale, Is.EqualTo(TimelineOptions.MaxScale));
            Assert.That(InView((start + end) / 2, scale, offset), Is.EqualTo(500).Within(0.01));
        });
    }

    [AvaloniaTest]
    public void A_range_too_long_for_the_smallest_scale_starts_at_the_margin()
    {
        (float scale, double offset) = TimelineHelper.FitRange(Range(1e8, 1e9), 1000, Margin);
        Assert.Multiple(() =>
        {
            Assert.That(scale, Is.EqualTo(TimelineOptions.MinScale));
            Assert.That(InView(1e8, scale, offset), Is.EqualTo(Margin).Within(0.01));
        });
    }

    [AvaloniaTest]
    public void A_zoom_keeps_one_point_in_place_and_ends_at_the_target()
    {
        // 375 px at scale 1 is 75 px into both views.
        var motion = new TimelineHelper.ZoomMotion(1, new Vector(300, 40), 0.2f, new Vector(0, 100));
        (float startScale, Vector start) = motion.At(0);
        (float middleScale, Vector middle) = motion.At(0.5);
        (float endScale, Vector end) = motion.At(1);
        Assert.Multiple(() =>
        {
            Assert.That(startScale, Is.EqualTo(1).Within(1e-6));
            Assert.That(start.X, Is.EqualTo(300).Within(1e-6));
            Assert.That(start.Y, Is.EqualTo(40).Within(1e-6));
            Assert.That(middleScale, Is.EqualTo(Math.Sqrt(0.2)).Within(1e-6));
            Assert.That(375 * middleScale - middle.X, Is.EqualTo(75).Within(1e-4));
            Assert.That(middle.Y, Is.EqualTo(70).Within(1e-6));
            Assert.That(endScale, Is.EqualTo(0.2).Within(1e-6));
            Assert.That(end.X, Is.EqualTo(0).Within(1e-4));
            Assert.That(end.Y, Is.EqualTo(100).Within(1e-6));
        });
    }

    [AvaloniaTest]
    public void A_zoom_at_one_scale_scrolls()
    {
        var motion = new TimelineHelper.ZoomMotion(1, new Vector(0, 0), 1, new Vector(500, 0));
        (float scale, Vector offset) = motion.At(0.5);
        Assert.Multiple(() =>
        {
            Assert.That(scale, Is.EqualTo(1));
            Assert.That(offset.X, Is.EqualTo(250).Within(1e-9));
        });
    }

    [AvaloniaTest]
    [TestCase(0d)]
    [TestCase(10d)]
    public async Task Fit_all_fits_the_scene_and_keeps_the_layers_in_view(double start)
    {
        using TimelineTestHost host = await TimelineTestHost.CreateAsync("timeline-fit-all", MediaTime.FromSeconds(30),
            [Clip(0, 5, 0), Clip(4, 8, 1), Clip(12, 18, 2)], theme: ThemeVariant.Dark);
        host.Scene.Start.Value = MediaTime.FromSeconds(start);
        host.Model.Options.Value = host.Model.Options.Value with { Offset = new Vector2(300, 25) };
        HeadlessTestHelpers.Render();

        await TimelineTabExtension.Instance.RunAsync(host.Model, new ContextCommandExecution("timeline.fitAll"));
        await RenderUntilFittedAsync(host, Range(start, start + 30));
        HeadlessCapture.SaveIfRequested(host.View, nameof(TimelineFitTests), $"fit-all-{start}");

        double width = host.Content.Viewport.Width;
        Assert.Multiple(() =>
        {
            Assert.That(InView(host, start), Is.EqualTo(start == 0 ? 0 : Margin).Within(0.5));
            Assert.That(InView(host, start + 30), Is.EqualTo(width - Margin).Within(0.5));
            Assert.That(host.Content.Offset.Y, Is.EqualTo(25));
            Assert.That(host.Model.Options.Value.Offset.X, Is.EqualTo(host.Content.Offset.X).Within(0.5));
        });
    }

    [AvaloniaTest]
    public async Task Fit_selection_fits_the_selected_elements_and_shows_their_top_layer()
    {
        using TimelineTestHost host = await TimelineTestHost.CreateAsync("timeline-fit-selection", MediaTime.FromSeconds(60),
            [Clip(2, 2, 2), Clip(6, 3, 5), Clip(20, 5, 1)], theme: ThemeVariant.Dark);
        List<ElementViewModel> elements = host.Model.AllElements();
        host.Model.SelectElement(elements[0]);
        host.Model.SelectElement(elements[1]);
        host.Model.Options.Value = host.Model.Options.Value with
        {
            Offset = new Vector2(3000, (float)host.Model.CalculateLayerTop(20))
        };
        HeadlessTestHelpers.Render();

        await TimelineTabExtension.Instance.RunAsync(host.Model, new ContextCommandExecution("timeline.fitSelection"));
        await RenderUntilFittedAsync(host, Range(2, 9));
        HeadlessCapture.SaveIfRequested(host.View, nameof(TimelineFitTests), "fit-selection");

        double width = host.Content.Viewport.Width;
        Assert.Multiple(() =>
        {
            Assert.That(InView(host, 2), Is.EqualTo(Margin).Within(0.5));
            Assert.That(InView(host, 9), Is.EqualTo(width - Margin).Within(0.5));
            Assert.That(host.Content.Offset.Y, Is.EqualTo(host.Model.CalculateLayerTop(2)).Within(0.5));
        });
    }

    [AvaloniaTest]
    public async Task Fit_selection_waits_for_a_selection_and_the_keys_stay_in_a_text_box()
    {
        using TimelineTestHost host = await TimelineTestHost.CreateAsync("timeline-fit-commands", MediaTime.FromSeconds(30),
            [Clip(2, 2, 0)]);
        var fitSelection = new ContextCommandExecution("timeline.fitSelection");
        Assert.Multiple(() =>
        {
            Assert.That(host.Model.FitSelection.CanExecute(), Is.False);
            Assert.That(TimelineTabExtension.Instance.CanRun(host.Model, fitSelection), Is.False);
        });

        host.Model.SelectElement(host.Model.AllElements()[0]);
        Assert.Multiple(() =>
        {
            Assert.That(host.Model.FitSelection.CanExecute(), Is.True);
            Assert.That(TimelineTabExtension.Instance.CanRun(host.Model, fitSelection), Is.True);
            foreach (string id in new[] { "timeline.fitAll", "timeline.fitSelection" })
            {
                var typed = new ContextCommandExecution(id)
                {
                    KeyEventArgs = new KeyEventArgs { Key = Key.F, Source = new TextBox() }
                };
                Assert.That(TimelineTabExtension.Instance.CanRun(host.Model, typed), Is.False, id);
            }
        });
    }

    [AvaloniaTest]
    public async Task The_zoom_menu_starts_with_the_fits_and_their_keys()
    {
        using TimelineTestHost host = await TimelineTestHost.CreateAsync("timeline-fit-menu", MediaTime.FromSeconds(30),
            [Clip(2, 2, 0)], theme: ThemeVariant.Dark);
        Point point = host.Timeline.TranslatePoint(new Point(600, 100), host.Window)!.Value;
        TimelineTestHost.WaitForHitTest(host.Window, host.Timeline, point);
        host.Window.MouseDown(point, MouseButton.Right);
        host.Window.MouseUp(point, MouseButton.Right);
        HeadlessTestHelpers.Render(3);
        FAMenuFlyoutSubItem menu = host.View.FindControl<FAMenuFlyoutSubItem>("ZoomMenu")!;
        Assert.That(menu.Focus(), Is.True);
        TopLevel root = TopLevel.GetTopLevel(menu)!;
        root.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        root.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
        object[] items = [.. menu.Items.Cast<object>().Take(3)];
        await HeadlessTestHelpers.WaitUntilAsync(() => TopLevel.GetTopLevel((Visual)items[0]) is not null);
        HeadlessTestHelpers.Render(3);
        HeadlessCapture.SaveIfRequested(menu, nameof(TimelineFitTests), "zoom-menu");

        Assert.Multiple(() =>
        {
            Assert.That(items[0], Is.InstanceOf<FAMenuFlyoutItem>());
            Assert.That(items[1], Is.InstanceOf<FAMenuFlyoutItem>());
            Assert.That(items[2], Is.InstanceOf<FAMenuFlyoutSeparator>());
            var fitAll = (FAMenuFlyoutItem)items[0];
            var fitSelection = (FAMenuFlyoutItem)items[1];
            Assert.That(fitAll.Text, Is.EqualTo(Strings.GraphFitAll));
            Assert.That(fitAll.InputGesture, Is.EqualTo(KeyGesture.Parse("F")));
            Assert.That(fitAll.IsEffectivelyEnabled, Is.True);
            Assert.That(fitSelection.Text, Is.EqualTo(Strings.GraphFitSelection));
            Assert.That(fitSelection.InputGesture, Is.EqualTo(KeyGesture.Parse("Shift+F")));
            Assert.That(fitSelection.IsEffectivelyEnabled, Is.False, "Nothing is selected.");
        });
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Another_zoom_takes_over_from_a_fit_in_progress(bool key)
    {
        using TimelineTestHost host = await TimelineTestHost.CreateAsync("timeline-fit-takeover", MediaTime.FromSeconds(30));
        Point point = host.Content.TranslatePoint(new Point(100, 80), host.Window)!.Value;
        TimelineTestHost.WaitForHitTest(host.Window, host.Content, point);

        await TimelineTabExtension.Instance.RunAsync(host.Model, new ContextCommandExecution("timeline.fitAll"));
        if (key)
            await TimelineTabExtension.Instance.RunAsync(host.Model, new ContextCommandExecution("timeline.zoomIn"));
        else
            host.Window.MouseWheel(point, new Vector(0, 1), TimelineTestHost.CommandModifier);

        // Longer than the zoom, which would have reached its scale by then.
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(0.8))
        {
            HeadlessTestHelpers.Render();
            await Task.Delay(16);
        }

        Assert.That(host.Model.Options.Value.Scale, Is.EqualTo(1.2f).Within(0.001));
    }

    // As when the palette fits a timeline that another tab hides: the view that shows it next takes the fit.
    [AvaloniaTest]
    public async Task A_fit_waits_for_a_view_to_show_it()
    {
        using TimelineTestHost host = await TimelineTestHost.CreateAsync("timeline-fit-hidden", MediaTime.FromSeconds(30));
        host.View.DataContext = null;

        await TimelineTabExtension.Instance.RunAsync(host.Model, new ContextCommandExecution("timeline.fitAll"));
        HeadlessTestHelpers.Render();
        Assert.Multiple(() =>
        {
            Assert.That(host.Model.PendingFit.Value, Is.Not.Null);
            Assert.That(host.Model.Options.Value.Scale, Is.EqualTo(1));
        });

        host.View.DataContext = host.Model;
        await RenderUntilFittedAsync(host, Range(0, 30));
        Assert.Multiple(() =>
        {
            Assert.That(host.Model.PendingFit.Value, Is.Null);
            Assert.That(InView(host, 30), Is.EqualTo(host.Content.Viewport.Width - Margin).Within(0.5));
        });
    }

    // The zoom runs on the animation clock, which keeps real time. It ends on the fitted scale exactly.
    private static Task RenderUntilFittedAsync(TimelineTestHost host, TimeRange range)
    {
        float scale = TimelineHelper.FitRange(range, host.Content.Viewport.Width, Margin).Scale;
        return HeadlessTestHelpers.WaitUntilAsync(() =>
        {
            HeadlessTestHelpers.Render();
            return host.Model.Options.Value.Scale == scale;
        });
    }

    private static ElementDescription Clip(double start, double length, int layer)
        => new(MediaTime.FromSeconds(start), MediaTime.FromSeconds(length), layer,
            new ElementSource.EngineObject(() => new RectShape()));

    private static TimeRange Range(double start, double end)
        => TimeRange.FromRange(MediaTime.FromSeconds(start), MediaTime.FromSeconds(end));

    private static double InView(double seconds, float scale, double offset)
        => MediaTime.FromSeconds(seconds).TimeToPixel(scale) - offset;

    // The time's distance from the left edge of the view.
    private static double InView(TimelineTestHost host, double seconds)
        => InView(seconds, host.Model.Options.Value.Scale, host.Content.Offset.X);
}
