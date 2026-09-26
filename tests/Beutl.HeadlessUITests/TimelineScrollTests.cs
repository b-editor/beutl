using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Vector2 = System.Numerics.Vector2;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class TimelineScrollTests
{
    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Touchpad_preserves_gesture_axes_and_direction(bool swap, bool ruler)
    {
        using TimelineSession session = await TimelineSession.CreateAsync(_ => true, swap);
        session.Capture($"before-{swap}-{ruler}");
        foreach (RawInputModifiers modifiers in new[] { RawInputModifiers.None, RawInputModifiers.Shift })
        {
            foreach (Vector delta in new[] { new Vector(0, -1), new Vector(-1, 0), new Vector(-0.4, -0.7), new Vector(0.25, 0.5) })
            {
                session.ResetOffset();
                session.Scroll(delta, ruler, modifiers);
                Assert.Multiple(() =>
                {
                    Assert.That(session.Content.Offset.X, Is.EqualTo(300 - delta.X * 50).Within(0.001));
                    Assert.That(session.Content.Offset.Y, Is.EqualTo(200 - delta.Y * 50).Within(0.001));
                    Assert.That(session.Pane.Offset.Y, Is.EqualTo(session.Content.Offset.Y).Within(0.001));
                    Assert.That(session.Model.Options.Value.Scale, Is.EqualTo(1));
                });
            }
        }
        session.Capture($"after-{swap}-{ruler}");
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Mouse_keeps_scroll_setting_and_shift_behavior(bool swap)
    {
        // The public constructor also exercises the native detector's headless fallback.
        using TimelineSession session = await TimelineSession.CreateAsync(null, swap);
        foreach (RawInputModifiers modifiers in new[] { RawInputModifiers.None, RawInputModifiers.Shift })
        {
            session.ResetOffset();
            session.Scroll(new Vector(0, -0.25), false, modifiers);
            bool vertical = swap ^ (OperatingSystem.IsWindows() && modifiers == RawInputModifiers.Shift);
            Assert.Multiple(() =>
            {
                Assert.That(session.Content.Offset.X, Is.EqualTo(vertical ? 300 : 312.5).Within(0.001));
                Assert.That(session.Content.Offset.Y, Is.EqualTo(vertical ? 212.5 : 200).Within(0.001));
            });
        }
    }

    [AvaloniaTest]
    [Platform("MacOSX")]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Precise_mouse_wheel_respects_axis_setting_in_content_and_ruler(bool swap, bool ruler)
    {
        nint nativeEvent = 0;
        using TimelineSession session = await TimelineSession.CreateAsync(
            _ => NativeScrollInput.MacOS.IsGestureScrollEvent(nativeEvent, 5000), swap);
        session.Capture($"mouse-before-{swap}-{ruler}");
        NativeScrollInputTests.WithMacScrollEvent(0, 0, 0, currentEvent =>
        {
            nativeEvent = currentEvent;
            session.Scroll(new Vector(0, -0.2), ruler);
            Assert.Multiple(() =>
            {
                Assert.That(session.Content.Offset.X, Is.EqualTo(swap ? 300 : 310).Within(0.001));
                Assert.That(session.Content.Offset.Y, Is.EqualTo(swap ? 210 : 200).Within(0.001));
                Assert.That(session.Pane.Offset.Y, Is.EqualTo(session.Content.Offset.Y).Within(0.001));
            });
        });
        session.Capture($"mouse-after-{swap}-{ruler}");
    }

    [AvaloniaTest]
    public async Task Input_source_is_checked_for_each_scroll()
    {
        bool touchpad = true;
        using TimelineSession session = await TimelineSession.CreateAsync(_ => touchpad, false);
        session.Scroll(new Vector(0, -1), false);
        Assert.That(session.Content.Offset, Is.EqualTo(new Vector(300, 250)));
        touchpad = false;
        session.Scroll(new Vector(0, -1), false);
        Assert.That(session.Content.Offset, Is.EqualTo(new Vector(350, 250)));
        touchpad = true;
        session.Scroll(new Vector(-1, 0), true);
        Assert.That(session.Content.Offset, Is.EqualTo(new Vector(400, 250)));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Command_scroll_keeps_zoom_anchored_to_pointer(bool ruler)
    {
        using TimelineSession session = await TimelineSession.CreateAsync(
            _ => throw new AssertionException("Zoom must not depend on the scroll device."), false);
        RawInputModifiers modifier = KeyGestureHelper.GetCommandModifier() == KeyModifiers.Meta
            ? RawInputModifiers.Meta : RawInputModifiers.Control;
        session.Scroll(new Vector(0, 1), ruler, modifier);
        Assert.Multiple(() =>
        {
            Assert.That(session.Model.Options.Value.Scale, Is.EqualTo(1.2f).Within(0.0001));
            Assert.That((session.Content.Offset.X + 100) / session.Model.Options.Value.Scale,
                Is.EqualTo(400).Within(0.001));
            Assert.That(session.Content.Offset.Y, Is.EqualTo(200));
        });
    }

    private sealed class TimelineSession : IDisposable
    {
        private readonly bool _originalSwap = GlobalConfiguration.Instance.EditorConfig.SwapTimelineScrollDirection;
        private readonly TimelineTabView _view;
        private readonly Window _window;

        private TimelineSession(TimelineTabViewModel model, Func<PointerWheelEventArgs, bool>? detector, bool swap)
        {
            Model = model;
            _view = detector is null ? new TimelineTabView() : new TimelineTabView(detector);
            _view.DataContext = model;
            _window = new Window { Content = _view, Width = 960, Height = 420 };
            GlobalConfiguration.Instance.EditorConfig.SwapTimelineScrollDirection = swap;
            _window.Show();
            HeadlessTestHelpers.Render();
            Content = _view.FindControl<ScrollViewer>("ContentScroll")!;
            Pane = _view.FindControl<ScrollViewer>("PaneScroll")!;
            ResetOffset();
        }

        public TimelineTabViewModel Model { get; }
        public ScrollViewer Content { get; }
        public ScrollViewer Pane { get; }

        public static async Task<TimelineSession> CreateAsync(Func<PointerWheelEventArgs, bool>? detector, bool swap)
        {
            await TestReset.ResetShellAsync();
            string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"timeline-scroll-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "timeline-scroll", root))!;
            Scene scene = project.Items.OfType<Scene>().Single();
            scene.Duration = TimeSpan.FromSeconds(30);
            TestShell.Editor.ActivateTabItem(scene);
            HeadlessTestHelpers.Settle();
            var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
            return new TimelineSession(editor.FindToolTab<TimelineTabViewModel>()!, detector, swap);
        }

        public void ResetOffset()
        {
            Model.Options.Value = Model.Options.Value with { Scale = 1, Offset = new Vector2(300, 200) };
            HeadlessTestHelpers.Render();
            Assert.That(Content.Offset, Is.EqualTo(new Vector(300, 200)));
        }

        public void Scroll(Vector delta, bool ruler, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Control target = ruler ? _view.FindControl<Border>("RulerBar")! : Content;
            Point point = target.TranslatePoint(new Point(100, ruler ? 4 : 80), _window)!.Value;
            _window.MouseWheel(point, delta, modifiers);
            HeadlessTestHelpers.Render();
        }

        public void Capture(string name)
        {
            if (Environment.GetEnvironmentVariable("BEUTL_TIMELINE_SCROLL_CAPTURE") is not { Length: > 0 } directory) return;
            Directory.CreateDirectory(directory);
            using var image = _window.CaptureRenderedFrame();
            image?.Save(Path.Combine(directory, $"{name}.png"), PngBitmapEncoderOptions.Default);
        }

        public void Dispose()
        {
            _view.DataContext = null;
            _window.Close();
            GlobalConfiguration.Instance.EditorConfig.SwapTimelineScrollDirection = _originalSwap;
        }
    }
}
