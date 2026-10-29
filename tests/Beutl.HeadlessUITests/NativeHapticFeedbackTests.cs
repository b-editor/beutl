using System.Runtime.InteropServices;
using Avalonia.Headless.NUnit;
using Beutl.Controls;
using static Beutl.HeadlessUITests.MacNative;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class NativeHapticFeedbackTests
{
    [AvaloniaTest]
    [Platform("MacOSX")]
    [TestCase(6u, 3L, true)] // Trackpad drag.
    [TestCase(5u, 3L, true)] // Trackpad movement.
    [TestCase(1u, 3L, true)] // Trackpad press.
    [TestCase(6u, 0L, false)] // Mouse drag, even with a finger resting on the trackpad.
    [TestCase(6u, 1L, false)] // Tablet pen.
    [TestCase(6u, 2L, false)] // Tablet proximity.
    [TestCase(22u, 3L, false)] // A scroll event is not a pointer drag.
    public void Only_the_matching_native_trackpad_mouse_event_allows_feedback(uint type, long subtype, bool expected)
    {
        // Construct events without posting them or emitting physical feedback.
        NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
        nint pool = Send(GetClass("NSAutoreleasePool"), Selector("new"));
        nint cgEvent = CGEventCreate(0);
        try
        {
            Assert.That(cgEvent, Is.Not.EqualTo(nint.Zero));
            CGEventSetType(cgEvent, type);
            CGEventSetIntegerValueField(cgEvent, 7, subtype); // kCGMouseEventSubtype
            CGEventSetTimestamp(cgEvent, 5_000_000_000); // Nanoseconds.
            nint nativeEvent = Send(GetClass("NSEvent"), Selector("eventWithCGEvent:"), cgEvent);
            Assert.That(nativeEvent, Is.Not.EqualTo(nint.Zero));
            Assert.Multiple(() =>
            {
                Assert.That(NativeHapticFeedback.MacOS.IsTrackpadMouseEvent(nativeEvent, 5000), Is.EqualTo(expected));
                Assert.That(NativeHapticFeedback.MacOS.IsTrackpadMouseEvent(nativeEvent, 5001), Is.False,
                    "A previous trackpad event must not classify a later mouse or keyboard operation.");
                Assert.That(NativeHapticFeedback.MacOS.IsTrackpadMouseEvent(0, 5000), Is.False);
            });
        }
        finally
        {
            if (cgEvent != 0) CFRelease(cgEvent);
            Send(pool, Selector("drain"));
        }
    }
}
