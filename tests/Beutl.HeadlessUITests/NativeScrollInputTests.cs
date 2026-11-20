using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Testing.Headless;
using static Beutl.HeadlessUITests.MacNative;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class NativeScrollInputTests
{
    [Test]
    [TestCase(true)]
    [TestCase(false)]
    public void Windows_optional_api_prefers_name_and_falls_back_to_documented_ordinal(bool namedExport)
    {
        List<bool> calls = [];
        NativeScrollInput.Windows.RegisterTouchpadThread native = enable => { calls.Add(enable); return true; };
        nint pointer = Marshal.GetFunctionPointerForDelegate(native);
        int ordinalLookups = 0;
        Func<bool, bool>? register = NativeScrollInput.Windows.LoadTouchpadRegistration(123, (module, name) =>
        {
            Assert.That(module, Is.EqualTo((nint)123));
            Assert.That(name, Is.EqualTo("RegisterTouchpadCapableThread"));
            return namedExport ? pointer : 0;
        }, (module, ordinal) =>
        {
            ordinalLookups++;
            Assert.That(module, Is.EqualTo((nint)123));
            Assert.That(ordinal, Is.EqualTo((nint)2688));
            return pointer;
        });
        Assert.That(register, Is.Not.Null);
        Assert.That(register!(true), Is.True);
        Assert.That(register(false), Is.True);
        Assert.That(calls, Is.EqualTo(new[] { true, false }));
        Assert.That(ordinalLookups, Is.EqualTo(namedExport ? 0 : 1));
        GC.KeepAlive(native);
    }

    [Test]
    public void Windows_missing_module_or_entry_point_keeps_registration_optional()
    {
        Assert.That(NativeScrollInput.Windows.LoadTouchpadRegistration(0,
            (_, _) => throw new AssertionException("A missing module must not be queried."),
            (_, _) => throw new AssertionException("A missing module must not be queried.")), Is.Null);
        Assert.That(NativeScrollInput.Windows.LoadTouchpadRegistration(123, (_, _) => 0, (_, _) => 0), Is.Null);
    }

    [Test]
    [TestCase(12345, 12.345678901, true, 1, 0, true)]
    [TestCase(12345, 12.345678901, true, 0, 1, true)]
    [TestCase(12345, 12.345678901, true, 0, 0, false)]
    [TestCase(12345, 12.345678901, false, 1, 0, false)]
    [TestCase(12344, 12.345678901, true, 1, 0, false)]
    [TestCase(12346, 12.345678901, true, 1, 0, false)]
    public void Gesture_metadata_requires_matching_timestamp_precision_and_a_gesture_phase(
        int timestamp, double nativeTimestamp, bool precise, int phase, int momentum, bool expected)
    {
        Assert.That(NativeScrollInput.IsMacGestureScroll((ulong)timestamp, nativeTimestamp, precise, phase, momentum),
            Is.EqualTo(expected));
    }

    [AvaloniaTest]
    public void Missing_or_closed_event_roots_use_mouse_axes_without_native_access()
    {
        using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        var window = new Window();
        var detached = new Border();
        try
        {
            foreach (object? source in new object?[] { null, new object(), detached })
            {
                var e = new PointerWheelEventArgs(source, pointer, detached, default, 1, default, KeyModifiers.None, new Vector(0, 1));
                Assert.That(NativeScrollInput.UsesGestureAxes(e), Is.False);
            }
            window.Show();
            window.Close();
            var closedEvent = new PointerWheelEventArgs(window, pointer, window, default, 1, default, KeyModifiers.None, new Vector(0, 1));
            Assert.That(NativeScrollInput.UsesGestureAxes(closedEvent), Is.False);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void Timeline_registers_before_input_and_releases_on_detach_and_window_close()
    {
        bool registered = false;
        int registrations = 0;
        int removals = 0;
        var view = new TimelineTabView(_ => false, _ => NativeScrollInput.Windows.RegisterInput(enable =>
        {
            registered = enable;
            if (enable) registrations++;
            else removals++;
            return true;
        }, () => { }, () => { }));
        var first = new Window { Content = view };
        var second = new Window();
        try
        {
            first.Show();
            Assert.That(registered, Is.True, "The native registration must precede the first input event.");
            first.Content = null;
            Assert.That(registered, Is.False);
            second.Content = view;
            second.Show();
            Assert.That(registered, Is.True);
            second.Close();
            Assert.Multiple(() =>
            {
                Assert.That(registered, Is.False);
                Assert.That(registrations, Is.EqualTo(2));
                Assert.That(removals, Is.EqualTo(2));
            });
        }
        finally
        {
            first.Close();
            second.Close();
        }
    }

    [Test]
    [TestCase(0x10, true)]
    [TestCase(0x02, false)]
    [TestCase(-1, false)]
    public void Windows_registration_is_active_before_native_message_delivery(int deviceType, bool expected)
    {
        List<string> calls = [];
        bool registered = false;
        IDisposable? registration = NativeScrollInput.Windows.RegisterInput(enable =>
        {
            calls.Add(enable ? "register" : "unregister");
            registered = enable;
            return true;
        }, () => calls.Add("hook"), () => calls.Add("unhook"));

        calls.Add("dispatch");
        uint? recordedSource = !registered ? 0x02u : deviceType < 0 ? null : (uint)deviceType;
        bool result = NativeScrollInput.Windows.IsTouchpadScroll(() =>
        {
            calls.Add("query");
            return recordedSource;
        });
        Assert.That(registered, Is.True);
        registration!.Dispose();
        registration.Dispose();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(expected));
            Assert.That(registered, Is.False);
            Assert.That(calls, Is.EqualTo(new[] { "hook", "register", "dispatch", "query", "unregister", "unhook" }));
        });
    }

    [Test]
    public void Windows_does_not_unregister_when_registration_is_unavailable_or_fails()
    {
        List<string> calls = [];
        Assert.That(NativeScrollInput.Windows.RegisterInput(null, () => calls.Add("hook"), () => calls.Add("unhook")), Is.Null);
        Assert.That(calls, Is.Empty);
        Assert.That(NativeScrollInput.Windows.RegisterInput(enable =>
        {
            Assert.That(enable, Is.True, "Failed registration must not decrement the thread's registration count.");
            return false;
        }, () => calls.Add("hook"), () => calls.Add("unhook")), Is.Null);
        Assert.That(calls, Is.EqualTo(new[] { "hook", "unhook" }));
    }

    [Test]
    public void Windows_restores_thread_registration_when_query_throws()
    {
        List<bool> calls = [];
        Assert.Throws<InvalidOperationException>(() =>
        {
            using IDisposable? registration = NativeScrollInput.Windows.RegisterInput(enable =>
            {
                calls.Add(enable);
                return true;
            }, () => { }, () => { });
            NativeScrollInput.Windows.IsTouchpadScroll(() => throw new InvalidOperationException());
        });
        Assert.That(calls, Is.EqualTo(new[] { true, false }));
    }

    [Test]
    [TestCase(0x0245u)] // WM_POINTERUPDATE
    [TestCase(0x0246u)] // WM_POINTERDOWN
    [TestCase(0x0247u)] // WM_POINTERUP
    public void Windows_touchpad_contacts_use_default_processing_before_Avalonia(uint message)
    {
        bool handled = false;
        int conversions = 0;
        nint result = NativeScrollInput.Windows.ForwardTouchpadMessage(123, message, 0x12340007, 456,
            ref handled, id => { Assert.That(id, Is.EqualTo(7)); return 5; }, (hwnd, msg, wParam, lParam) =>
            {
                conversions++;
                Assert.That((hwnd, msg, wParam, lParam), Is.EqualTo(((nint)123, message, (nint)0x12340007, (nint)456)));
                return 789;
            });
        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.True);
            Assert.That(conversions, Is.EqualTo(1));
            Assert.That(result, Is.EqualTo((nint)789));
        });
    }

    [Test]
    [TestCase(0x020au, 5)] // Converted WM_MOUSEWHEEL must reach Avalonia.
    [TestCase(0x020eu, 5)] // Converted WM_MOUSEHWHEEL must reach Avalonia.
    [TestCase(0x0246u, 2)] // Touchscreen.
    [TestCase(0x0246u, 3)] // Pen.
    [TestCase(0x0246u, 4)] // Mouse.
    [TestCase(0x0246u, -1)] // Unknown source.
    public void Windows_preserves_other_pointer_and_wheel_delivery(uint message, int type)
    {
        bool handled = false;
        NativeScrollInput.Windows.ForwardTouchpadMessage(0, message, 1, 0, ref handled,
            _ => type < 0 ? null : (uint)type, (_, _, _, _) => throw new AssertionException("Unexpected gesture conversion."));
        Assert.That(handled, Is.False);
    }

    [AvaloniaTest]
    [TestCase(0x0008, 120, false, KeyModifiers.None)] // A pinch: MK_CONTROL and a whole notch.
    [TestCase(0x0008, -45, false, KeyModifiers.None)] // A fine pinch inward.
    [TestCase(0x0000, 30, true, KeyModifiers.None)] // Ctrl reported only by the key state.
    [TestCase(0x000c, 120, false, KeyModifiers.Shift)] // MK_SHIFT | MK_CONTROL keeps Shift.
    public void Windows_touchpad_zoom_reaches_the_view_as_a_pinch(int keys, int delta, bool controlKey, KeyModifiers modifiers)
    {
        var target = new Border { Background = Brushes.Transparent };
        var window = new Window { Content = target, Width = 400, Height = 300 };
        PointerDeltaEventArgs? pinch = null;
        target.AddHandler(InputElement.PointerTouchPadGestureMagnifyEvent, (_, e) => pinch = e);
        var point = new Point(120, 80);
        ShowForInput(window, point);
        try
        {
            bool converted = NativeScrollInput.Windows.ConvertTouchpadZoom(window, 0x020a, WheelParam(keys, delta),
                ScreenParam(window, point), () => 0x10, key => controlKey && key == 0x11, 42);
            Assert.That(converted, Is.True, "The wheel message must not reach Avalonia as well.");
            Assert.That(pinch, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(pinch!.Source, Is.SameAs(target));
                Assert.That(pinch.Delta.X, Is.EqualTo(Math.Pow(1.2, delta / 120.0) - 1).Within(1e-12),
                    "A pinch must zoom as far as the same Ctrl + wheel did.");
                Assert.That(pinch.Delta.Y, Is.EqualTo(pinch.Delta.X));
                Assert.That(pinch.GetPosition(window), Is.EqualTo(point));
                Assert.That(pinch.KeyModifiers, Is.EqualTo(modifiers));
                Assert.That(pinch.Timestamp, Is.EqualTo(42));
            });
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void Windows_touchpad_zoom_keeps_held_modifiers_except_the_gestures_ctrl()
    {
        var window = new Window { Content = new Border { Background = Brushes.Transparent } };
        KeyModifiers? modifiers = null;
        window.AddHandler(InputElement.PointerTouchPadGestureMagnifyEvent, (_, e) => modifiers = e.KeyModifiers);
        ShowForInput(window, new Point(10, 10));
        try
        {
            // Ctrl, Alt and the right Windows key are down.
            NativeScrollInput.Windows.ConvertTouchpadZoom(window, 0x020a, WheelParam(0x0008, 120),
                ScreenParam(window, new Point(10, 10)), () => 0x10, key => key is 0x11 or 0x12 or 0x5c, 1);
            Assert.That(modifiers, Is.EqualTo(KeyModifiers.Alt | KeyModifiers.Meta));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void Windows_touchpad_zoom_goes_to_the_first_enabled_ancestor()
    {
        var disabled = new Border { Background = Brushes.Transparent, IsEnabled = false };
        var parent = new Panel { Children = { disabled } };
        var window = new Window { Content = parent };
        object? source = null;
        window.AddHandler(InputElement.PointerTouchPadGestureMagnifyEvent, (_, e) => source = e.Source);
        ShowForInput(window, new Point(10, 10));
        try
        {
            NativeScrollInput.Windows.ConvertTouchpadZoom(window, 0x020a, WheelParam(0x0008, 120),
                ScreenParam(window, new Point(10, 10)), () => 0x10, _ => false, 1);
            Assert.That(source, Is.SameAs(parent), "Avalonia routes native pointer input past disabled elements the same way.");
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    [TestCase(0x020au, 0x0008, 0x02)] // A mouse's Ctrl + wheel keeps zooming through the wheel handlers.
    [TestCase(0x020au, 0x0008, -1)] // An unidentified source.
    [TestCase(0x020au, 0x0000, 0x10)] // A touchpad scroll.
    [TestCase(0x020eu, 0x0008, 0x10)] // A horizontal touchpad scroll with Ctrl held.
    [TestCase(0x0246u, 0x0008, 0x10)] // WM_POINTERDOWN.
    public void Windows_other_wheels_reach_Avalonia_unchanged(uint message, int keys, int deviceType)
    {
        var window = new Window { Content = new Border { Background = Brushes.Transparent } };
        int pinches = 0;
        window.AddHandler(InputElement.PointerTouchPadGestureMagnifyEvent, (_, _) => pinches++);
        ShowForInput(window, new Point(10, 10));
        try
        {
            bool converted = NativeScrollInput.Windows.ConvertTouchpadZoom(window, message, WheelParam(keys, 120),
                ScreenParam(window, new Point(10, 10)), () => deviceType < 0 ? null : (uint)deviceType, _ => false, 1);
            Assert.That(converted, Is.False);
            Assert.That(pinches, Is.Zero);
        }
        finally { window.Close(); }
    }

    // WM_MOUSEWHEEL packs the key flags and the wheel delta into wParam, and the screen position into lParam.
    internal static nint WheelParam(int keys, int delta) => (nint)(((delta & 0xffff) << 16) | (keys & 0xffff));

    internal static nint ScreenParam(TopLevel root, Point point)
    {
        PixelPoint screen = root.PointToScreen(point);
        return (nint)(((screen.Y & 0xffff) << 16) | (screen.X & 0xffff));
    }

    // Hit testing finds nothing until the window has rendered a frame, which can take several ticks.
    private static void ShowForInput(Window window, Point point)
    {
        window.Show();
        for (int i = 0; i < 20 && window.InputHitTest(point, enabledElementsOnly: false) == null; i++)
        {
            HeadlessTestHelpers.Render();
        }

        Assert.That(window.InputHitTest(point, enabledElementsOnly: false), Is.Not.Null, "The window never rendered a frame.");
    }

    [AvaloniaTest]
    [Platform("MacOSX")]
    [TestCase(0u, 0L, 0L, false)] // A high-resolution mouse wheel is not a touch gesture.
    [TestCase(0u, 1L, 0L, true)] // Gesture began.
    [TestCase(0u, 2L, 0L, true)] // Gesture changed.
    [TestCase(0u, 0L, 2L, true)] // Momentum continues using the gesture axes.
    [TestCase(1u, 0L, 0L, false)] // Line scrolling (ordinary wheel).
    public void MacOS_distinguishes_native_gestures_from_precise_mouse_wheels(uint units, long phase, long momentumPhase, bool expected)
    {
        WithMacScrollEvent(units, phase, momentumPhase, nativeEvent =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(NativeScrollInput.MacOS.IsGestureScrollEvent(nativeEvent, 5000), Is.EqualTo(expected));
                Assert.That(NativeScrollInput.MacOS.IsGestureScrollEvent(nativeEvent, 5001), Is.False,
                    "An earlier native event must not classify a later managed event.");
                Assert.That(NativeScrollInput.MacOS.IsGestureScrollEvent(0, 5000), Is.False);
            });
        });
    }

    internal static void WithMacScrollEvent(uint units, long phase, long momentumPhase, Action<nint> action)
    {
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
        nint pool = Send(GetClass("NSAutoreleasePool"), Selector("new"));
        nint cgEvent = CGEventCreateScrollWheelEvent2(0, units, 1, -10, 0, 0);
        try
        {
            Assert.That(cgEvent, Is.Not.EqualTo(nint.Zero));
            CGEventSetTimestamp(cgEvent, 5_000_000_000); // Nanoseconds since boot.
            CGEventSetIntegerValueField(cgEvent, 99, phase); // kCGScrollWheelEventScrollPhase
            CGEventSetIntegerValueField(cgEvent, 123, momentumPhase); // kCGScrollWheelEventMomentumPhase
            nint nativeEvent = Send(GetClass("NSEvent"), Selector("eventWithCGEvent:"), cgEvent);
            Assert.That(nativeEvent, Is.Not.EqualTo(nint.Zero));
            action(nativeEvent);
        }
        finally
        {
            if (cgEvent != 0) CFRelease(cgEvent);
            Send(pool, Selector("drain"));
        }
    }
}
