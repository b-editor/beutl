using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Components.Views;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public class TimelineDragReadoutTests
{
    [AvaloniaTest]
    [TestCase("left", "-15f", 0.5, 2.5)]
    [TestCase("right", "+15f", 1, 2.5)]
    [TestCase("move", "+15f", 1.5, 2)]
    [TestCase("roll", "+15f", 1, 2.5)]
    [TestCase("slide", "+15f", 1.5, 2)]
    public async Task DragReadout_ShowsTheCommittedValues(string gesture, string change, double start, double length)
    {
        using var configuration = await ResetWithFormat(TimeDisplayFormat.Frames);
        List<ElementDescription> clips = [Clip(1, 2, 0)];
        if (gesture is "roll" or "slide") clips.Add(Clip(3, 2, 0));
        if (gesture == "slide") clips.Add(Clip(0, 1, 0));

        using var host = await TimelineTestHost.CreateAsync($"drag-readout-{gesture}", Seconds(10), clips);
        host.Model.IsSnapEnabled.Value = false;
        host.Model.IsRippleEnabled.Value = false;
        host.Model.IsRollMode.Value = gesture == "roll";
        host.Model.IsSlideMode.Value = gesture == "slide";
        Element element = host.Scene.Children.Single(clip => clip.Start.Value == Seconds(1));
        Element? back = host.Scene.Children.FirstOrDefault(clip => clip.Start.Value == Seconds(3));
        TimelineOverlay overlay = GetOverlay(host);

        Point press = PressClip(host, element, gesture);
        Assert.That(overlay.DragReadout, Is.Null);
        Point release = gesture switch
        {
            "left" => press.WithX(WindowPosition(host, Seconds(0.5)).X),
            "right" => press.WithX(WindowPosition(host, Seconds(3.5)).X),
            "move" => press + new Vector(Seconds(0.5).TimeToPixel(1), host.Model.LayerRowHeight.Value),
            _ => press + new Vector(Seconds(0.5).TimeToPixel(1), 0)
        };
        MovePointer(host, release);
        var readout = overlay.DragReadout;
        Assert.That(readout?.Change, Is.EqualTo(change));
        Assert.That(element.Start.Value, Is.EqualTo(Seconds(1)));
        Assert.That(element.Length.Value, Is.EqualTo(Seconds(2)));
        if (gesture == "roll")
            HeadlessCapture.SaveIfRequested(host.Window, nameof(TimelineDragReadoutTests), "roll");

        host.Window.MouseUp(release, MouseButton.Left);
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(overlay.DragReadout, Is.Null);
            Assert.That(element.Start.Value, Is.EqualTo(Seconds(start)));
            Assert.That(element.Length.Value, Is.EqualTo(Seconds(length)));
            Assert.That(element.ZIndex.Value, Is.EqualTo(gesture == "move" ? 1 : 0));
            string details = gesture switch
            {
                "left" => string.Format(Strings.TimelineDrag_StartLength, Frames(element.Start.Value), Frames(element.Length.Value)),
                "right" => string.Format(Strings.TimelineDrag_EndLength, Frames(element.Range.End), Frames(element.Length.Value)),
                "move" => string.Format(Strings.TimelineDrag_StartLayer, Frames(element.Start.Value), "1"),
                "roll" => string.Format(Strings.TimelineDrag_RollLengths, Frames(element.Length.Value), Frames(back!.Length.Value)),
                _ => string.Format(Strings.TimelineDrag_StartEnd, Frames(element.Start.Value), Frames(element.Range.End))
            };
            Assert.That(readout?.Details, Is.EqualTo(details));
        });
    }

    [AvaloniaTest]
    public async Task MoveReadout_UsesTheSelectionBoundary()
    {
        using var configuration = await ResetWithFormat(TimeDisplayFormat.Frames);
        using var host = await TimelineTestHost.CreateAsync("drag-readout-group", Seconds(10), [Clip(1, 1, 0), Clip(5, 1, 1)], width: 1200);
        host.Model.IsSnapEnabled.Value = false;
        Element first = host.Scene.Children.Single(clip => clip.ZIndex.Value == 0);
        Element anchor = host.Scene.Children.Single(clip => clip.ZIndex.Value == 1);
        ElementViewModel firstModel = host.Model.GetViewModelFor(first)!;
        ElementViewModel anchorModel = host.Model.GetViewModelFor(anchor)!;
        host.Model.ClearSelected();
        host.Model.SelectElement(firstModel);
        host.Model.SelectElement(anchorModel);
        anchorModel.GroupSelectedElements.Execute();

        Point press = PressClip(host, anchor, "move");
        Point release = press - new Vector(Seconds(3).TimeToPixel(1), 0);
        MovePointer(host, release);
        TimelineOverlay overlay = GetOverlay(host);
        Assert.That(overlay.DragReadout?.Change, Is.EqualTo("-30f"));
        Assert.That(overlay.DragReadout?.Details, Is.EqualTo(string.Format(Strings.TimelineDrag_StartLayer, "120f", "1")));

        host.Window.MouseUp(release, MouseButton.Left);
        HeadlessTestHelpers.Settle();
        Assert.That(first.Start.Value, Is.EqualTo(MediaTime.Zero));
        Assert.That(anchor.Start.Value, Is.EqualTo(Seconds(4)));
        Assert.That(overlay.DragReadout, Is.Null);
    }

    [AvaloniaTest]
    public async Task TrimReadout_UsesTheLockedRippleBoundary()
    {
        using var configuration = await ResetWithFormat(TimeDisplayFormat.Frames);
        using var host = await TimelineTestHost.CreateAsync("drag-readout-ripple", Seconds(10), [Clip(0, 2, 0), Clip(2, 2, 0), Clip(5, 2, 0)]);
        host.Model.IsSnapEnabled.Value = false;
        host.Model.IsRippleEnabled.Value = true;
        Element target = host.Scene.Children.Single(clip => clip.Start.Value == MediaTime.Zero);
        Element follower = host.Scene.Children.Single(clip => clip.Start.Value == Seconds(2));
        Element locked = host.Scene.Children.Single(clip => clip.Start.Value == Seconds(5));
        locked.IsLocked.Value = true;
        PressClip(host, target, "right");
        Point release = WindowPosition(host, Seconds(4));
        MovePointer(host, release);

        TimelineOverlay overlay = GetOverlay(host);
        Assert.That(overlay.DragReadout?.Change, Is.EqualTo("+30f"));
        Assert.That(overlay.DragReadout?.Details, Is.EqualTo(string.Format(Strings.TimelineDrag_EndLength, "90f", "90f")));
        host.Window.MouseUp(release, MouseButton.Left);
        HeadlessTestHelpers.Settle();
        Assert.That(target.Range.End, Is.EqualTo(Seconds(3)));
        Assert.That(follower.Range.End, Is.EqualTo(Seconds(5)));
        Assert.That(locked.Start.Value, Is.EqualTo(Seconds(5)));
        Assert.That(overlay.DragReadout, Is.Null);
    }

    [AvaloniaTest]
    public async Task DragReadout_FitsTheScrolledViewportAndDisappearsOnEscape()
    {
        using var configuration = await ResetWithFormat(TimeDisplayFormat.Timecode);
        using var host = await TimelineTestHost.CreateAsync("drag-readout-viewport", Seconds(20), [Clip(3, 2, 0)],
            width: 760, theme: ThemeVariant.Light);
        host.Model.IsSnapEnabled.Value = false;
        host.Content.Offset = new Vector(180, 0);
        HeadlessTestHelpers.Render(4);
        Element element = host.Scene.Children.Single();
        PressClip(host, element, "move");
        Point release = host.Timeline.TranslatePoint(new Point(host.Content.Offset.X + host.Content.Viewport.Width - 6,
            host.Content.Viewport.Height - 6), host.Window)!.Value;
        MovePointer(host, release);

        TimelineOverlay overlay = GetOverlay(host);
        Assert.That(overlay.DragReadout?.Change, Does.Match(@"^[+-]00:00:\d\d:\d\d$"));
        Point pointer = overlay.DragReadout!.Value.Position;
        Rect bounds = overlay.GetReadoutBounds(pointer)!.Value;
        Assert.That(bounds.Right, Is.LessThan(overlay.Offset.X + pointer.X));
        Assert.That(bounds.Bottom, Is.LessThan(overlay.Offset.Y + pointer.Y));

        host.Content.Offset += new Vector(100, 0);
        HeadlessTestHelpers.Render(4);
        Assert.That(overlay.DragReadout?.Position, Is.EqualTo(pointer));
        Rect afterScroll = overlay.GetReadoutBounds(pointer)!.Value;
        Assert.That(afterScroll.X - bounds.X, Is.EqualTo(100).Within(0.01));
        Assert.That(afterScroll.Size, Is.EqualTo(bounds.Size));
        HeadlessCapture.SaveIfRequested(host.Window, nameof(TimelineDragReadoutTests), "scrolled-viewport");

        host.Window.KeyPress(Key.Escape, RawInputModifiers.LeftMouseButton, PhysicalKey.Escape, null);
        host.Window.MouseUp(release, MouseButton.Left);
        HeadlessTestHelpers.Settle();
        Assert.That(overlay.DragReadout, Is.Null);
        Assert.That(element.Start.Value, Is.EqualTo(Seconds(3)));
        Assert.That(element.ZIndex.Value, Is.Zero);
    }

    private static Point PressClip(TimelineTestHost host, Element element, string gesture)
    {
        var border = host.View.GetVisualDescendants().OfType<ElementView>()
            .Single(view => (view.DataContext as ElementViewModel)?.Model == element).FindControl<Border>("border")!;
        double x = gesture switch
        {
            "left" => 2,
            "right" or "roll" => border.Bounds.Width - 2,
            _ => border.Bounds.Width / 2
        };
        Point press = border.TranslatePoint(new Point(x, border.Bounds.Height / 2), host.Window)!.Value;
        TimelineTestHost.WaitForHitTest(host.Window, border, press);
        host.Window.MouseMove(press);
        host.Window.MouseDown(press, MouseButton.Left);
        return press;
    }

    private static void MovePointer(TimelineTestHost host, Point point)
    {
        host.Window.MouseMove(point, RawInputModifiers.LeftMouseButton);
        HeadlessTestHelpers.Render(4);
    }

    private static async Task<IDisposable> ResetWithFormat(TimeDisplayFormat format)
    {
        await TestReset.ResetShellAsync();
        var setting = TestShell.Configuration.EditorConfig.TimeDisplayFormat;
        TimeDisplayFormat original = setting.Value;
        setting.Value = format;
        return System.Reactive.Disposables.Disposable.Create(() => setting.Value = original);
    }

    private static Point WindowPosition(TimelineTestHost host, MediaTime time)
        => host.Timeline.TranslatePoint(new Point(time.TimeToPixel(1), host.Model.LayerRowHeight.Value / 2), host.Window)!.Value;

    private static TimelineOverlay GetOverlay(TimelineTestHost host) => host.View.FindControl<TimelineOverlay>("overlay")!;

    private static MediaTime Seconds(double value) => MediaTime.FromSeconds(value);

    private static string Frames(MediaTime value) => TimeDisplayFormatter.Format(value, new Rational(30, 1), TimeDisplayFormat.Frames) + "f";

    private static ElementDescription Clip(double start, double length, int layer)
        => new(Seconds(start), Seconds(length), layer, new ElementSource.EngineObject(() => new RectShape()));
}
