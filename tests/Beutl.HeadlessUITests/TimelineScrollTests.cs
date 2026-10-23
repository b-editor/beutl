using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
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

    private sealed class TimelineSession : IDisposable
    {
        private readonly bool _originalSwap = GlobalConfiguration.Instance.EditorConfig.SwapTimelineScrollDirection.Value;
        private readonly TimelineTestHost _host;

        private TimelineSession(TimelineTestHost host, bool swap)
        {
            _host = host;
            GlobalConfiguration.Instance.EditorConfig.SwapTimelineScrollDirection.Value = swap;
            Pane = host.View.FindControl<ScrollViewer>("PaneScroll")!;
            ResetOffset();
        }

        public TimelineTabViewModel Model => _host.Model;
        public ScrollViewer Content => _host.Content;
        public ScrollViewer Pane { get; }

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

        public void Scroll(Vector delta, bool ruler, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            Control target = ruler ? _host.RulerBar : Content;
            Point point = target.TranslatePoint(new Point(100, ruler ? 4 : 80), _host.Window)!.Value;
            _host.Window.MouseWheel(point, delta, modifiers);
            HeadlessTestHelpers.Render();
        }

        public void Dispose()
        {
            _host.Dispose();
            GlobalConfiguration.Instance.EditorConfig.SwapTimelineScrollDirection.Value = _originalSwap;
        }
    }
}
