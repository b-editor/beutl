using System.Runtime.InteropServices;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.Helpers;

namespace Beutl.HeadlessUITests;

[TestFixture]
public partial class NativeScrollInputTests
{
    [Test]
    [TestCase(0x10, true)]
    [TestCase(0x02, false)]
    [TestCase(-1, false)]
    public void Windows_registration_is_limited_to_the_source_query(int deviceType, bool expected)
    {
        List<string> calls = [];
        bool result = NativeScrollInput.Windows.IsTouchpadScroll(enable =>
        {
            calls.Add(enable ? "register" : "unregister");
            return true;
        }, () =>
        {
            calls.Add("query");
            return deviceType < 0 ? null : (uint)deviceType;
        });
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(expected));
            Assert.That(calls, Is.EqualTo(new[] { "register", "query", "unregister" }));
        });
    }

    [Test]
    public void Windows_does_not_unregister_when_registration_is_unavailable_or_fails()
    {
        Assert.That(NativeScrollInput.Windows.IsTouchpadScroll(null, () => 0x02), Is.False);
        Assert.That(NativeScrollInput.Windows.IsTouchpadScroll(enable =>
        {
            Assert.That(enable, Is.True, "Failed registration must not decrement the thread's registration count.");
            return false;
        }, () => 0x02), Is.False);
    }

    [Test]
    public void Windows_restores_thread_registration_when_query_throws()
    {
        List<bool> calls = [];
        Assert.Throws<InvalidOperationException>(() => NativeScrollInput.Windows.IsTouchpadScroll(enable =>
        {
            calls.Add(enable);
            return true;
        }, () => throw new InvalidOperationException()));
        Assert.That(calls, Is.EqualTo(new[] { true, false }));
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

    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [LibraryImport(ObjC, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetClass(string name);

    [LibraryImport(ObjC, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint Selector(string name);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector);

    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial nint Send(nint receiver, nint selector, nint argument);

    [LibraryImport(CoreGraphics)]
    private static partial nint CGEventCreateScrollWheelEvent2(nint source, uint units, uint wheelCount, int wheel1, int wheel2, int wheel3);

    [LibraryImport(CoreGraphics)]
    private static partial void CGEventSetTimestamp(nint cgEvent, ulong timestamp);

    [LibraryImport(CoreGraphics)]
    private static partial void CGEventSetIntegerValueField(nint cgEvent, uint field, long value);

    [LibraryImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    private static partial void CFRelease(nint value);
}
