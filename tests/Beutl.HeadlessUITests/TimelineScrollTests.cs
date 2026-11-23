using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
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
        session.Scroll(new Vector(0, 1), ruler, TimelineTestHost.CommandModifier);
        Assert.Multiple(() =>
        {
            Assert.That(session.Model.Options.Value.Scale, Is.EqualTo(1.2f).Within(0.0001));
            Assert.That((session.Content.Offset.X + 100) / session.Model.Options.Value.Scale,
                Is.EqualTo(400).Within(0.001));
            Assert.That(session.Content.Offset.Y, Is.EqualTo(200));
        });
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Touchpad_pinch_keeps_zoom_anchored_to_pointer(bool ruler)
    {
        using TimelineSession session = await TimelineSession.CreateAsync(null, false);
        double scale = 1;
        foreach (double delta in new[] { 0.2, -0.1, 0.001 })
        {
            PointerDeltaEventArgs args = session.Pinch(delta, ruler);
            scale *= 1 + delta;
            Assert.Multiple(() =>
            {
                Assert.That(args.Handled, Is.True);
                Assert.That(session.Model.Options.Value.Scale, Is.EqualTo(scale).Within(0.0001));
                Assert.That((session.Content.Offset.X + 100) / session.Model.Options.Value.Scale,
                    Is.EqualTo(400).Within(0.001));
                Assert.That(session.Content.Offset.Y, Is.EqualTo(200));
            });
        }
    }

    [AvaloniaTest]
    [TestCase(true, false)]
    [TestCase(false, false)]
    [TestCase(false, true)]
    public async Task Touchscreen_pinch_keeps_the_initial_center_after_scroll(bool ruler, bool startWithPan)
    {
        using TimelineSession session = await TimelineSession.CreateAsync(null, false);
        Point first = session.TouchPoint(100, ruler);
        Point second = session.TouchPoint(200, ruler);
        var left = session.Window.TouchBegin(first);
        if (startWithPan)
        {
            first -= new Vector(20, 0);
            session.Window.TouchMove(left, first);
            HeadlessTestHelpers.Render();
            Assert.That(session.Content.Offset.X, Is.GreaterThan(300));
        }

        double center = startWithPan ? 140 : 150;
        double initialDistance = ((Vector)(second - first)).Length;
        double anchor = session.Content.Offset.X + center;
        var right = session.Window.TouchBegin(second);
        Point end = second + new Vector(25, 0);
        session.Window.TouchMove(right, second + new Vector(10, 0));
        HeadlessTestHelpers.Render();
        session.Window.TouchMove(right, end);
        HeadlessTestHelpers.Render();

        Assert.That(session.Model.Options.Value.Scale, Is.EqualTo(1 + 25 / initialDistance).Within(0.001));
        Assert.That((session.Content.Offset.X + center) / session.Model.Options.Value.Scale, Is.EqualTo(anchor).Within(1));
        session.Window.TouchEnd(right, end);
        session.Window.TouchEnd(left, first);
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Windows_touchpad_zoom_zooms_like_a_pinch(bool ruler)
    {
        using TimelineSession session = await TimelineSession.CreateAsync(null, false);
        double scale = 1;
        // Both steps start from a whole offset: from a fractional one the view zooms around the panel's
        // pixel-rounded position, a few thousandths of a pixel away, whatever the input.
        foreach (int delta in new[] { 120, -60 })
        {
            double x = session.WindowsTouchpadZoom(delta, ruler);
            scale *= Math.Pow(1.2, delta / 120.0);
            Assert.Multiple(() =>
            {
                Assert.That(session.Model.Options.Value.Scale, Is.EqualTo(scale).Within(0.0001));
                Assert.That((session.Content.Offset.X + x) / session.Model.Options.Value.Scale,
                    Is.EqualTo(300 + x).Within(0.001));
                Assert.That(session.Content.Offset.Y, Is.EqualTo(200));
            });
        }
    }

    [AvaloniaTest]
    public async Task Touchpad_pinch_clamps_and_ignores_invalid_magnification()
    {
        using TimelineSession session = await TimelineSession.CreateAsync(null, false);
        session.Pinch(1000, ruler: false);
        Assert.That(session.Model.Options.Value.Scale, Is.EqualTo(TimelineOptions.MaxScale));

        for (int i = 0; i < 10 && session.Model.Options.Value.Scale > TimelineOptions.MinScale; i++)
        {
            session.Pinch(-0.99, ruler: false);
        }
        Assert.That(session.Model.Options.Value.Scale, Is.EqualTo(TimelineOptions.MinScale));

        session.ResetOffset();
        foreach (double delta in new[] { -1, -2, double.NaN })
        {
            PointerDeltaEventArgs args = session.Pinch(delta, ruler: false);
            Assert.Multiple(() =>
            {
                Assert.That(args.Handled, Is.True);
                Assert.That(session.Model.Options.Value.Scale, Is.EqualTo(1));
                Assert.That(session.Content.Offset, Is.EqualTo(new Vector(300, 200)));
            });
        }
    }

    private sealed class TimelineSession : IDisposable
    {
        private readonly bool _originalSwap = TestShell.Configuration.EditorConfig.SwapTimelineScrollDirection.Value;
        private readonly TimelineTestHost _host;

        private TimelineSession(TimelineTestHost host, bool swap)
        {
            _host = host;
            TestShell.Configuration.EditorConfig.SwapTimelineScrollDirection.Value = swap;
            Pane = host.View.FindControl<ScrollViewer>("PaneScroll")!;
            ResetOffset();
        }

        public TimelineTabViewModel Model => _host.Model;
        public ScrollViewer Content => _host.Content;
        public ScrollViewer Pane { get; }
        public Window Window => _host.Window;

        public static async Task<TimelineSession> CreateAsync(Func<PointerWheelEventArgs, bool>? detector, bool swap)
        {
            TimelineTestHost host = await TimelineTestHost.CreateAsync(
                "timeline-scroll", MediaTime.FromSeconds(30), scrollDetector: detector);
            return new TimelineSession(host, swap);
        }

        public void ResetOffset()
        {
            Model.Options.Value = Model.Options.Value with { Scale = 1, Offset = new Vector2(300, 200) };
            HeadlessTestHelpers.Render();
            Assert.That(Content.Offset, Is.EqualTo(new Vector(300, 200)));
        }

        public Point TouchPoint(double x, bool ruler)
        {
            Control target = ruler ? _host.RulerBar : Content;
            return target.TranslatePoint(new Point(x, ruler ? 4 : 80), Window)!.Value;
        }

        public void Scroll(Vector delta, bool ruler, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Control target = ruler ? _host.RulerBar : Content;
            Point point = target.TranslatePoint(new Point(100, ruler ? 4 : 80), _host.Window)!.Value;
            _host.Window.MouseWheel(point, delta, modifiers);
            HeadlessTestHelpers.Render();
        }

        public PointerDeltaEventArgs Pinch(double delta, bool ruler)
        {
            Control target = ruler ? _host.RulerBar : Content;
            Point point = target.TranslatePoint(new Point(100, ruler ? 4 : 80), _host.Window)!.Value;
            TimelineTestHost.WaitForHitTest(_host.Window, target, point);
            IInputElement source = _host.Window.InputHitTest(point)!;
            using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            var args = new PointerDeltaEventArgs(InputElement.PointerTouchPadGestureMagnifyEvent,
                source, pointer, _host.Window, point, 1, default, KeyModifiers.None, new Vector(delta, delta));
            source.RaiseEvent(args);
            HeadlessTestHelpers.Render();
            return args;
        }

        // Sends the Ctrl + wheel message that Windows makes from a precision touchpad pinch. The message
        // carries whole screen pixels, so this returns the x in the target where the view saw the pointer.
        public double WindowsTouchpadZoom(int delta, bool ruler)
        {
            Control target = ruler ? _host.RulerBar : Content;
            Point point = target.TranslatePoint(new Point(100, ruler ? 4 : 80), _host.Window)!.Value;
            TimelineTestHost.WaitForHitTest(_host.Window, target, point);
            Assert.That(NativeScrollInput.Windows.ConvertTouchpadZoom(_host.Window, 0x020a,
                NativeScrollInputTests.WheelParam(0x0008, delta), NativeScrollInputTests.ScreenParam(_host.Window, point),
                () => 0x10, _ => false, 1), Is.True);
            HeadlessTestHelpers.Render();
            Point seen = _host.Window.PointToClient(_host.Window.PointToScreen(point));
            return _host.Window.TranslatePoint(seen, target)!.Value.X;
        }

        public void Dispose()
        {
            _host.Dispose();
            TestShell.Configuration.EditorConfig.SwapTimelineScrollDirection.Value = _originalSwap;
        }
    }
}
